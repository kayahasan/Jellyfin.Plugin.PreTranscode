using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.PreTranscode.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.PreTranscode.Controllers;

public class NonStandardItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Codec { get; set; } = string.Empty;
    public double SizeMb { get; set; }
    public string? PosterUrl { get; set; }
    public string? Resolution { get; set; } // e.g. "4K", "1080p", "720p"
}

public class SeasonGroupDto
{
    public int SeasonNumber { get; set; }
    public List<NonStandardItemDto> Episodes { get; set; } = new();
}

public class SeriesDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public List<SeasonGroupDto> Seasons { get; set; } = new();
}

public class NonStandardItemsResponse
{
    public List<NonStandardItemDto> Movies { get; set; } = new();
    public List<SeriesDto> Series { get; set; } = new();
}

public class EncodeRequestDto
{
    public List<Guid> ItemIds { get; set; } = new();
    public bool ForceReencode { get; set; } // Zaten hedef codec'te olsa bile yeniden kodla
}

public class EncodePreviewDto
{
    public int TotalItems { get; set; }
    public int ToEncode { get; set; }
    public int AlreadyTargetCodec { get; set; }
    public double TotalSizeMb { get; set; }
    public List<string> SkippedItems { get; set; } = new();
}

[ApiController]
[Route("Plugins/PreTranscode")]
[Authorize]
public class PreTranscodeController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly JobQueueService _jobQueueService;

    public PreTranscodeController(ILibraryManager libraryManager, JobQueueService jobQueueService)
    {
        _libraryManager = libraryManager;
        _jobQueueService = jobQueueService;
    }

    [HttpGet("Items")]
    public ActionResult<NonStandardItemsResponse> GetNonStandardItems()
    {
        var config = Plugin.Instance!.Configuration;

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode },
            IsVirtualItem = false,
            Recursive = true
        }).ToList();

        var response = new NonStandardItemsResponse();

        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Path) || !System.IO.File.Exists(item.Path))
            {
                continue;
            }

            // Minimum dosya boyutu filtresi
            long fileSize;
            try
            {
                fileSize = new FileInfo(item.Path).Length;
            }
            catch (IOException)
            {
                continue;
            }

            if (fileSize < config.MinimumFileSizeMb * 1024L * 1024L)
            {
                continue;
            }

            // Zaten optimize edilmiş dosya varsa atla
            var outputPath = GetOutputPath(item.Path, config);
            if (System.IO.File.Exists(outputPath))
            {
                continue;
            }

            // Hedef codec'te olup olmadığını kontrol et ama listeden çıkarma
            // Kullanıcı filtre ile seçebilir, encode sırasında tekrar kontrol edilir
            var videoStream = item.GetMediaStreams()?.FirstOrDefault(s => s.Type == MediaStreamType.Video);
            var resolution = GetResolution(videoStream);

            if (item is MediaBrowser.Controller.Entities.Movies.Movie)
            {
                response.Movies.Add(new NonStandardItemDto
                {
                    Id = item.Id,
                    Name = item.Name,
                    Path = item.Path,
                    Codec = videoStream?.Codec ?? "bilinmiyor",
                    SizeMb = Math.Round(fileSize / 1024.0 / 1024.0, 1),
                    PosterUrl = item.Id.ToString(),
                    Resolution = resolution
                });
            }
            else if (item is MediaBrowser.Controller.Entities.TV.Episode episode)
            {
                var series = episode.Series;
                if (series == null)
                {
                    continue;
                }

                var seriesDto = response.Series.FirstOrDefault(s => s.Id == series.Id)
                    ?? response.Series.AddOrNew(new SeriesDto { Id = series.Id, Name = series.Name, PosterUrl = series.Id.ToString() });

                var seasonNum = episode.ParentIndexNumber ?? 0;
                var epNum = episode.IndexNumber ?? 0;
                var season = seriesDto.Seasons.FirstOrDefault(s => s.SeasonNumber == seasonNum)
                    ?? seriesDto.Seasons.AddOrNew(new SeasonGroupDto { SeasonNumber = seasonNum });

                season.Episodes.Add(new NonStandardItemDto
                {
                    Id = item.Id,
                    Name = $"{epNum} - {item.Name}",
                    Path = item.Path,
                    Codec = videoStream?.Codec ?? "bilinmiyor",
                    SizeMb = Math.Round(fileSize / 1024.0 / 1024.0, 1),
                    Resolution = resolution
                });
            }
        }

        response.Movies.Sort((a, b) => b.SizeMb.CompareTo(a.SizeMb));
        response.Series.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var series in response.Series)
        {
            series.Seasons.Sort((a, b) => a.SeasonNumber.CompareTo(b.SeasonNumber));
            foreach (var season in series.Seasons)
            {
                season.Episodes.Sort((a, b) =>
{
// "1 - Episode Name" formatinda - ilk kismi numerik sirala
if (int.TryParse(a.Name.Split('-')[0].Trim(), out var na) &&
    int.TryParse(b.Name.Split('-')[0].Trim(), out var nb))
{
return na.CompareTo(nb);
}
return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
});
            }
        }

        return Ok(response);
    }

    [HttpGet("Jobs")]
    public ActionResult<IEnumerable<JobInfo>> GetJobs()
    {
        return Ok(_jobQueueService.GetAllJobs());
    }

    [HttpPost("Encode")]
    public ActionResult Encode([FromBody] EncodeRequestDto request)
    {
        var config = Plugin.Instance!.Configuration;
        var queued = new List<Guid>();
        var skipped = new List<string>();

        foreach (var itemId in request.ItemIds)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item is null || string.IsNullOrEmpty(item.Path))
            {
                continue;
            }

            // Zaten hedef codec'te mi?
            if (!request.ForceReencode && IsAlreadyTargetCodec(item, config))
            {
                skipped.Add($"{item.Name} (zaten {config.TargetVideoCodec.ToUpper()})");
                continue;
            }

            _jobQueueService.Enqueue(item.Id, item.Name, item.Path, config);
            queued.Add(itemId);
        }

        return Ok(new { queued = queued.Count, skippedCount = skipped.Count, skipped });
    }

    [HttpPost("EncodePreview")]
    public ActionResult<EncodePreviewDto> EncodePreview([FromBody] EncodeRequestDto request)
    {
        var config = Plugin.Instance!.Configuration;
        var preview = new EncodePreviewDto();
        var toEncode = new List<Guid>();

        foreach (var itemId in request.ItemIds)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item is null || string.IsNullOrEmpty(item.Path))
            {
                continue;
            }

            preview.TotalItems++;

            try
            {
                var fileSize = new System.IO.FileInfo(item.Path).Length;
                preview.TotalSizeMb += fileSize / 1024.0 / 1024.0;
            }
            catch { }

            if (IsAlreadyTargetCodec(item, config))
            {
                preview.AlreadyTargetCodec++;
                preview.SkippedItems.Add($"{item.Name} (zaten {config.TargetVideoCodec.ToUpper()})");
            }
            else
            {
                toEncode.Add(itemId);
            }
        }

        preview.ToEncode = toEncode.Count;
        return Ok(preview);
    }

    [HttpPost("Cancel/{itemId}")]
    public ActionResult Cancel([FromRoute] Guid itemId)
    {
        _jobQueueService.CancelJob(itemId);
        return Ok(new { cancelled = true });
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
        return Path.Combine(directory, $"{nameNoExt}-optimized.mkv");
    }

    private static string? GetResolution(dynamic? videoStream)
    {
        if (videoStream is null) return null;
        var width = videoStream.Width ?? 0;
        var height = videoStream.Height ?? 0;
        
        if (width >= 3840 || height >= 2160) return "4K";
        if (width >= 1920 || height >= 1080) return "1080p";
        if (width >= 1280 || height >= 720) return "720p";
        return null;
    }

}

// Extension methods
internal static class ListExtensions
{
    public static T AddOrNew<T>(this List<T> list, T item) where T : new()
    {
        list.Add(item);
        return item;
    }
}
