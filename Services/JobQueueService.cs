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
    Failed
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
}

/// <summary>
/// Kullanıcının dashboard'dan seçtiği öğeleri sıraya alır ve
/// MaxConcurrentJobs limitine göre arka planda kodlar.
/// </summary>
public class JobQueueService
{
    private readonly ILogger<JobQueueService> _logger;
    private readonly EncoderService _encoderService;
    private readonly ConcurrentDictionary<Guid, JobInfo> _jobs = new();
    private SemaphoreSlim _semaphore = new(1, 1);
    private int _currentLimit = 1;

    public JobQueueService(ILogger<JobQueueService> logger, EncoderService encoderService)
    {
        _logger = logger;
        _encoderService = encoderService;
    }

    public System.Collections.Generic.IReadOnlyCollection<JobInfo> GetAllJobs() => (System.Collections.Generic.IReadOnlyCollection<JobInfo>)_jobs.Values;

    public void Enqueue(Guid itemId, string name, string sourcePath, PluginConfiguration config)
    {
        if (_jobs.TryGetValue(itemId, out var existing) && existing.Status is JobStatus.Queued or JobStatus.Running)
        {
            return; // zaten kuyrukta
        }

        EnsureConcurrencyLimit(config.MaxConcurrentJobs);

        var outputPath = GetOutputPath(sourcePath, config);
        var sourceSize = new FileInfo(sourcePath).Length;
        var job = new JobInfo
        {
            ItemId = itemId,
            Name = name,
            Status = JobStatus.Queued,
            OutputPath = outputPath,
            SourceSizeMb = Math.Round(sourceSize / 1024.0 / 1024.0, 1)
        };
        _jobs[itemId] = job;

        _ = RunJobAsync(job, sourcePath, outputPath, sourceSize, config);
    }

    private void EnsureConcurrencyLimit(int limit)
    {
        limit = Math.Max(1, limit);
        if (limit != _currentLimit)
        {
            _semaphore = new SemaphoreSlim(limit, limit);
            _currentLimit = limit;
        }
    }

    private async Task RunJobAsync(JobInfo job, string sourcePath, string outputPath, long sourceSize, PluginConfiguration config)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        job.Status = JobStatus.Running;

        // Progress polling
        var progressCts = new CancellationTokenSource();
        var progressTask = Task.Run(async () =>
        {
            while (!progressCts.Token.IsCancellationRequested)
            {
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
                await Task.Delay(1000, progressCts.Token).ConfigureAwait(false);
            }
        }, progressCts.Token);

        try
        {
            var success = await _encoderService.TranscodeAsync(sourcePath, outputPath, config, CancellationToken.None).ConfigureAwait(false);
            job.Status = success ? JobStatus.Completed : JobStatus.Failed;
            job.ProgressPercent = success ? 100 : job.ProgressPercent;
            if (success)
            {
                try { job.OutputSizeMb = Math.Round(new FileInfo(outputPath).Length / 1024.0 / 1024.0, 1); } catch { }
            }
            if (!success)
            {
                job.Error = "ffmpeg başarısız oldu, sunucu loglarına bakın.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PreTranscode: iş hatası {Name}", job.Name);
            job.Status = JobStatus.Failed;
            job.Error = ex.Message;
        }
        finally
        {
            progressCts.Cancel();
            try { await progressTask.ConfigureAwait(false); } catch { }
            _semaphore.Release();
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
}
