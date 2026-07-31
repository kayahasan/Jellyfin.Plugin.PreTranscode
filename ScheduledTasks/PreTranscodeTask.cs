using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.PreTranscode.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreTranscode.ScheduledTasks;

public class PreTranscodeTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly EncoderService _encoderService;
    private readonly ILogger<PreTranscodeTask> _logger;

    public PreTranscodeTask(ILibraryManager libraryManager, EncoderService encoderService, ILogger<PreTranscodeTask> logger)
    {
        _libraryManager = libraryManager;
        _encoderService = encoderService;
        _logger = logger;
    }

    public string Name => "Kütüphaneyi Ön-Kodla";

    public string Key => "PreTranscodeLibraryScan";

    public string Description => "Kütüphanedeki video dosyalarını yapılandırılmış hedef codec/kaliteye göre arka planda kodlar.";

    public string Category => "PreTranscode";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;

        var items = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode },
            IsVirtualItem = false,
            Recursive = true
        }).Where(i => !string.IsNullOrEmpty(i.Path)).ToList();

        _logger.LogInformation("PreTranscode: {Count} öğe taranacak", items.Count);

        var processed = 0;
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;
            progress.Report(100.0 * processed / Math.Max(1, items.Count));

            try
            {
                await ProcessItemAsync(item, config, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "PreTranscode: {Path} işlenirken hata", item.Path);
            }
        }
    }

    private async Task ProcessItemAsync(BaseItem item, Configuration.PluginConfiguration config, CancellationToken cancellationToken)
    {
        var sourcePath = item.Path;
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            return;
        }

        var fileInfo = new FileInfo(sourcePath);
        if (fileInfo.Length < config.MinimumFileSizeMb * 1024L * 1024L)
        {
            return;
        }

        // Zaten üretilmiş bir companion dosya varsa atla.
        var outputPath = GetOutputPath(sourcePath, config);
        if (File.Exists(outputPath))
        {
            return;
        }

        if (config.SkipIfAlreadyTargetCodec && IsAlreadyTargetCodec(item, config))
        {
            return;
        }

        _logger.LogInformation("PreTranscode: işleniyor {Path}", sourcePath);
        var success = await _encoderService.TranscodeAsync(sourcePath, outputPath, config, cancellationToken).ConfigureAwait(false);

        if (success)
        {
            // Kütüphaneye yeni dosyayı bildir; Jellyfin bir sonraki taramada
            // aynı klasördeki dosyayı alternate version adayı olarak görecektir.
            // Otomatik "merge as alternate version" bağlama işlemi Jellyfin sürümüne göre
            // ILibraryManager / IProviderManager üzerinden ayrıca yazılmalı - burada yalnızca
            // dosya üretimi ve loglama yapılıyor.
            _logger.LogInformation("PreTranscode: tamamlandı, kütüphane taramasını bekliyor: {Output}", outputPath);
        }
    }

    private bool IsAlreadyTargetCodec(BaseItem item, Configuration.PluginConfiguration config)
    {
        var videoStream = item.GetMediaStreams()?.FirstOrDefault(s => s.Type == MediaStreamType.Video);
        if (videoStream is null)
        {
            return false;
        }

        var codec = videoStream.Codec?.ToLowerInvariant() ?? string.Empty;
        return config.TargetVideoCodec == "hevc"
            ? codec.Contains("hevc") || codec.Contains("h265")
            : codec.Contains("h264") || codec.Contains("avc");
    }

    private static string GetOutputPath(string sourcePath, Configuration.PluginConfiguration config)
    {
        var directory = string.IsNullOrEmpty(config.OutputDirectory)
            ? Path.GetDirectoryName(sourcePath)!
            : config.OutputDirectory;

        var nameNoExt = Path.GetFileNameWithoutExtension(sourcePath);
        var fileName = $"{nameNoExt}-optimized.mkv";
        return Path.Combine(directory, fileName);
    }
}
