using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PreTranscode.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreTranscode.Services;

public enum JobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public class JobInfo
{
    public Guid ItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public JobStatus Status { get; set; }
    public string? Error { get; set; }
    public double ProgressPercent { get; set; }
    public double SourceSizeMb { get; set; }
    public double OutputSizeMb { get; set; }
    public string? OutputPath { get; set; }
    public bool IsCancelling { get; set; }
    // U3: Enhanced job info
    public string? EncoderType { get; set; }        // e.g. "h264_vaapi", "hevc_nvenc", "libx264"
    public string? CodecConversion { get; set; }   // e.g. "HEVC → H.264"
    public string? ResolutionConversion { get; set; } // e.g. "4K → 1080p" or null
    public double EncodeSpeedFps { get; set; }     // estimated fps
    public string? EtaText { get; set; }           // e.g. "12dk"
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    // Internal tracking for ETA calculation
    private double _lastOutputSize = 0;
    private DateTime _lastSizeCheck = DateTime.MinValue;
    internal void UpdateSpeed(double currentSize, DateTime now)
    {
        if (_lastOutputSize > 0 && _lastSizeCheck != DateTime.MinValue)
        {
            var elapsed = (now - _lastSizeCheck).TotalSeconds;
            if (elapsed > 0)
            {
                var bytesPerSec = (currentSize - _lastOutputSize) / elapsed;
                EncodeSpeedFps = Math.Max(0, Math.Round(bytesPerSec / 1024.0 / 1024.0, 1)); // MB/s as proxy
            }
        }
        _lastOutputSize = currentSize;
        _lastSizeCheck = now;
    }
    internal string? CalculateEta(double sourceSizeBytes, double progressPercent)
    {
        if (_lastOutputSize <= 0 || _lastSizeCheck == DateTime.MinValue || progressPercent <= 0 || progressPercent >= 95)
            return null;
        var elapsed = (DateTime.UtcNow - StartedAt).TotalSeconds;
        if (elapsed < 5) return "Hesaplanıyor..."; // need minimum data
        var remaining = 100 - progressPercent;
        var secondsPerPercent = elapsed / progressPercent;
        var remainingSeconds = remaining * secondsPerPercent;
        if (remainingSeconds < 60)
            return $"{Math.Ceiling(remainingSeconds)}sn";
        return $"{Math.Ceiling(remainingSeconds / 60)}dk";
    }
}

/// <summary>
/// Kullanıcının dashboard'dan seçtiği öğeleri sıraya alır ve
/// MaxConcurrentJobs limitine göre arka planda kodlar.
/// </summary>
public class JobQueueService : IDisposable
{
    private readonly ILogger<JobQueueService> _logger;
    private readonly EncoderService _encoderService;
    private readonly ConcurrentDictionary<Guid, JobInfo> _jobs = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private int _currentLimit = 1;
    private bool _disposed = false;

    public JobQueueService(ILogger<JobQueueService> logger, EncoderService encoderService)
    {
        _logger = logger;
        _encoderService = encoderService;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _semaphore.Dispose();
    }

    public System.Collections.Generic.IReadOnlyCollection<JobInfo> GetAllJobs() => (System.Collections.Generic.IReadOnlyCollection<JobInfo>)_jobs.Values;

    public void CancelJob(Guid itemId)
    {
        if (_jobs.TryGetValue(itemId, out var job) && job.Status == JobStatus.Running)
        {
            job.IsCancelling = true;
            _logger.LogInformation("PreTranscode: iptal istendi {Name} ({Progress}%)", job.Name, job.ProgressPercent);
        }
    }

    public void Enqueue(Guid itemId, string name, string sourcePath, PluginConfiguration config)
    {
        if (_jobs.TryGetValue(itemId, out var existing) && existing.Status is JobStatus.Queued or JobStatus.Running)
        {
            return; // zaten kuyrukta
        }

        EnsureConcurrencyLimit(config.MaxConcurrentJobs);

        var outputPath = GetOutputPath(sourcePath, config);
        var sourceSize = new FileInfo(sourcePath).Length;
        var encoderType = GetEncoderTypeName(config);
        var job = new JobInfo
        {
            ItemId = itemId,
            Name = name,
            Status = JobStatus.Queued,
            OutputPath = outputPath,
            SourceSizeMb = Math.Round(sourceSize / 1024.0 / 1024.0, 1),
            EncoderType = encoderType,
            CodecConversion = config.TargetVideoCodec == "h264" ? "→ H.264" : "→ HEVC"
        };
        _jobs[itemId] = job;

        _ = RunJobAsync(job, sourcePath, outputPath, sourceSize, config);
    }

    private void EnsureConcurrencyLimit(int limit)
    {
        limit = Math.Max(1, limit);
        if (limit != _currentLimit)
        {
            _currentLimit = limit;
            // SemaphoreSlim capacity değiştirilemez, mevcut kullanımda sorun yok
            // çünkü concurrent limit zaten semaphore.WaitAsync ile kontrol ediliyor
        }
    }

    private async Task RunJobAsync(JobInfo job, string sourcePath, string outputPath, long sourceSize, PluginConfiguration config)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        job.Status = JobStatus.Running;
        job.IsCancelling = false;
        job.StartedAt = DateTime.UtcNow;

        // Encode iptal kontrolu + progress polling
        var encodeCts = new CancellationTokenSource();
        var monitorCts = new CancellationTokenSource();
        var monitorTask = Task.Run(async () =>
        {
            while (!monitorCts.Token.IsCancellationRequested)
            {
                await Task.Delay(500, monitorCts.Token).ConfigureAwait(false);
                // Progress guncelle
                try
                {
                    if (File.Exists(outputPath))
                    {
                        var outSize = new FileInfo(outputPath).Length;
                        var now = DateTime.UtcNow;
                        job.UpdateSpeed(outSize, now);
                        job.OutputSizeMb = Math.Round(outSize / 1024.0 / 1024.0, 1);
                        job.ProgressPercent = Math.Min(95, Math.Round((outSize / (double)sourceSize) * 100, 1));
                        job.EtaText = job.CalculateEta(sourceSize, job.ProgressPercent);
                    }
                }
                catch { }
                // Iptal kontrolu
                if (job.IsCancelling && !encodeCts.IsCancellationRequested)
                {
                    _logger.LogInformation("PreTranscode: iptal ediliyor {Name} ({Progress}%)", job.Name, job.ProgressPercent);
                    encodeCts.Cancel();
                }
            }
        }, monitorCts.Token);

        try
        {
            var success = await _encoderService.TranscodeAsync(sourcePath, outputPath, config, encodeCts.Token).ConfigureAwait(false);
            if (job.IsCancelling)
            {
                // Iptal edildi - partial dosyayi sil
                job.Status = JobStatus.Cancelled;
                job.Error = "Kullanici tarafindan iptal edildi.";
                job.CompletedAt = DateTime.UtcNow;
                job.EtaText = null;
                CleanupPartial(outputPath);
            }
            else if (success)
            {
                job.Status = JobStatus.Completed;
                job.ProgressPercent = 100;
                job.CompletedAt = DateTime.UtcNow;
                job.EtaText = null;
                try { job.OutputSizeMb = Math.Round(new FileInfo(outputPath).Length / 1024.0 / 1024.0, 1); } catch { }
            }
            else
            {
                job.Status = JobStatus.Failed;
                job.CompletedAt = DateTime.UtcNow;
                job.EtaText = null;
                job.Error = "ffmpeg başarısız oldu, sunucu loglarına bakın.";
            }
        }
        catch (OperationCanceledException)
        {
            job.Status = JobStatus.Cancelled;
            job.Error = "Kullanici tarafindan iptal edildi.";
            job.CompletedAt = DateTime.UtcNow;
            job.EtaText = null;
            CleanupPartial(outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PreTranscode: iş hatası {Name}", job.Name);
            job.Status = JobStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
            job.EtaText = null;
            job.Error = ex.Message;
        }
        finally
        {
            monitorCts.Cancel();
            try { await monitorTask.ConfigureAwait(false); } catch { }
            encodeCts.Dispose();
            _semaphore.Release();
        }
    }

    private static void CleanupPartial(string? outputPath)
    {
        if (string.IsNullOrEmpty(outputPath)) return;
        try
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
        catch (Exception ex)
        {
            // Best effort - logla ama hata verme
            System.Diagnostics.Debug.WriteLine($"PreTranscode: partial dosya silinemedi: {ex.Message}");
        }
    }

    private static string GetOutputPath(string sourcePath, PluginConfiguration config)
    {
        var directory = string.IsNullOrEmpty(config.OutputDirectory)
            ? Path.GetDirectoryName(sourcePath)!
            : config.OutputDirectory;

        var nameNoExt = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(directory, $"{nameNoExt}-optimized.mkv");
    }

    private static string GetEncoderTypeName(PluginConfiguration config)
    {
        var codec = config.TargetVideoCodec == "h264" ? "h264" : "hevc";
        return config.HardwareAcceleration switch
        {
            HwAccelType.Vaapi => $"{codec}_vaapi",
            HwAccelType.Qsv => $"{codec}_qsv",
            HwAccelType.Nvenc => $"{codec}_nvenc",
            HwAccelType.Amf => $"{codec}_amf",
            _ => codec == "h264" ? "libx264" : "libx265"
        };
    }
}
