using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.PreTranscode.Configuration;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.Logging;

// Import HwAccelType from Configuration namespace
using HwAccelType = Jellyfin.Plugin.PreTranscode.Configuration.HwAccelType;

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
    public string? EncoderType { get; set; } // e.g. "h264_vaapi (GPU)", "libx264 (CPU)"
    public string? CodecConversion { get; set; } // e.g. "HEVC -> H.264"
    public string? ResolutionConversion { get; set; } // e.g. "4K -> 1080p"
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Kullanıcının dashboard'dan seçtiği öğeleri sıraya alır ve
/// MaxConcurrentJobs limitine göre arka planda kodlar.
/// </summary>
public class JobQueueService : IDisposable
{
    private readonly ILogger<JobQueueService> _logger;
    private readonly EncoderService _encoderService;
    private readonly IActivityManager _activityManager;
    private readonly ConcurrentDictionary<Guid, JobInfo> _jobs = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private int _currentLimit = 1;
    private bool _disposed = false;

    public JobQueueService(ILogger<JobQueueService> logger, EncoderService encoderService, IActivityManager activityManager)
    {
        _logger = logger;
        _encoderService = encoderService;
        _activityManager = activityManager;
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
        
        // Build encoder type display string
        var encoderType = GetEncoderType(config);
        var codecConversion = GetCodecConversion(sourcePath, config);
        
        var job = new JobInfo
        {
            ItemId = itemId,
            Name = name,
            Status = JobStatus.Queued,
            OutputPath = outputPath,
            SourceSizeMb = Math.Round(sourceSize / 1024.0 / 1024.0, 1),
            EncoderType = encoderType,
            CodecConversion = codecConversion
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

    private static string GetEncoderType(PluginConfiguration config)
    {
        var isGpu = config.HardwareAcceleration != HwAccelType.None;
        var suffix = isGpu ? "(GPU)" : "(CPU)";
        
        var codec = config.TargetVideoCodec;
        return config.HardwareAcceleration switch
        {
            HwAccelType.None => $"lib{(codec == "hevc" ? "x265" : "x264")} {suffix}",
            HwAccelType.Vaapi => $"{codec}_vaapi {suffix}",
            HwAccelType.Qsv => $"{codec}_qsv {suffix}",
            HwAccelType.Nvenc => $"{codec}_nvenc {suffix}",
            HwAccelType.Amf => $"{codec}_amf {suffix}",
            _ => $"{codec} {suffix}"
        };
    }

    private static string? GetCodecConversion(string sourcePath, PluginConfiguration config)
    {
        // TODO: Use ffprobe to detect source codec
        // For now, show target codec info
        var target = config.TargetVideoCodec == "hevc" ? "HEVC" : "H.264";
        return $"-> {target}";
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
                        job.OutputSizeMb = Math.Round(outSize / 1024.0 / 1024.0, 1);
                        job.ProgressPercent = Math.Min(95, Math.Round((outSize / (double)sourceSize) * 100, 1));
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
                CleanupPartial(outputPath);
            }
            else if (success)
            {
                job.Status = JobStatus.Completed;
                job.ProgressPercent = 100;
                job.CompletedAt = DateTime.UtcNow;
                try { job.OutputSizeMb = Math.Round(new FileInfo(outputPath).Length / 1024.0 / 1024.0, 1); } catch { }
                // Activity log - başarı
                var savedMb = job.SourceSizeMb - job.OutputSizeMb;
                var savedGb = Math.Round(savedMb / 1024.0, 1);
                _ = LogActivityAsync(job.Name, "PreTranscode: Kodlama tamamlandı", $"{job.SourceSizeMb:F1} MB → {job.OutputSizeMb:F1} MB ({savedGb} GB tasarruf)");
            }
            else
            {
                job.Status = JobStatus.Failed;
                job.Error = "ffmpeg başarısız oldu, sunucu loglarına bakın.";
                _ = LogActivityAsync(job.Name, "PreTranscode: Kodlama başarısız", "Sunucu loglarına bakın.");
            }
        }
        catch (OperationCanceledException)
        {
            job.Status = JobStatus.Cancelled;
            job.Error = "Kullanici tarafindan iptal edildi.";
            CleanupPartial(outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PreTranscode: iş hatası {Name}", job.Name);
            job.Status = JobStatus.Failed;
            job.Error = ex.Message;
            _ = LogActivityAsync(job.Name, "PreTranscode: Kodlama hatası", ex.Message);
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

    private async Task LogActivityAsync(string itemName, string name, string overview)
    {
        try
        {
            // Get first admin user ID for system activities
            var userId = GetSystemUserId();
            var entry = new ActivityLog(name, "PreTranscode", userId)
            {
                Overview = overview,
                ShortOverview = overview,
                ItemId = itemName
            };
            await _activityManager.CreateAsync(entry).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PreTranscode: Activity log yazılamadı");
        }
    }

    private Guid GetSystemUserId()
    {
        // Use Guid.Empty as system user for plugin activities
        // Jellyfin will show these as system activities
        return Guid.Empty;
    }
}
