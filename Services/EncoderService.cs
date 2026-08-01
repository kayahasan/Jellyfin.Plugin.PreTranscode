using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PreTranscode.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreTranscode.Services;

public class EncoderService
{
    private readonly ILogger<EncoderService> _logger;
    private readonly IMediaEncoder _mediaEncoder;

    public EncoderService(ILogger<EncoderService> logger, IMediaEncoder mediaEncoder)
    {
        _logger = logger;
        _mediaEncoder = mediaEncoder;
    }

    private string FfmpegPath
    {
        get
        {
            if (!string.IsNullOrEmpty(_mediaEncoder.EncoderPath))
            {
                return _mediaEncoder.EncoderPath;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var jellyfinData = Environment.GetEnvironmentVariable("JELLYFIN_DATA_DIRECTORY") ?? "C:\\ProgramData\\Jellyfin\\Server";
                var candidates = new[]
                {
                    Path.Combine(jellyfinData, "ffmpeg", "ffmpeg.exe"),
                    Path.Combine(jellyfinData, "ffmpeg6", "ffmpeg.exe"),
                    Path.Combine(jellyfinData, "jellyfin-ffmpeg", "ffmpeg.exe"),
                    "ffmpeg.exe"
                };
                foreach (var path in candidates)
                {
                    if (TryFindExecutable(path)) return path;
                }
            }
            else
            {
                var candidates = new[]
                {
                    "/usr/lib/jellyfin-ffmpeg/ffmpeg",
                    "/usr/lib/jellyfin-ffmpeg/ffmpeg6",
                    "/usr/bin/ffmpeg"
                };
                foreach (var path in candidates)
                {
                    if (TryFindExecutable(path)) return path;
                }
            }

            return "ffmpeg";
        }
    }

    private bool TryFindExecutable(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "-version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = new Process { StartInfo = psi };
            p.Start();
            p.WaitForExit(3000);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// ffprobe ile kaynak dosyanin pix_fmt bilgisini alir.
    /// 10-bit formatlar: p010le, yuv420p10le, nv12, yuv420p, vb.
    /// </summary>
    private string? GetSourcePixelFormat(string sourcePath)
    {
        try
        {
            // ffprobe yolu: ffmpeg -> ffprobe (dosya adi), jellyfin-ffmpeg -> jellyfin-ffmpeg (dizin ayni kalir)
            var ffmpegDir = Path.GetDirectoryName(FfmpegPath) ?? ".";
            var ffprobeName = Path.GetFileName(FfmpegPath).Replace("ffmpeg", "ffprobe");
            var ffprobePath = Path.Combine(ffmpegDir, ffprobeName);

            var psi = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v error -select_streams v:0 -show_entries stream=pix_fmt -of default=noprint_wrappers=1:nokey=1 \"{sourcePath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = new Process { StartInfo = psi };
            p.Start();
            p.WaitForExit(10000);
            var output = p.StandardOutput.ReadToEnd().Trim();
            return string.IsNullOrEmpty(output) ? null : output;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PreTranscode: ffprobe pix_fmt alinamadi");
            return null;
        }
    }

    /// <summary>
    /// Hedef dizine yazma izinleri var mi kontrol eder.
    /// </summary>
    private bool CanWriteToDirectory(string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(dir)) return true;

        try
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Test dosyasi yaz ve sil
            var testFile = Path.Combine(dir, ".pretranscode_write_test_" + Guid.NewGuid());
            using (File.Create(testFile, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError("PreTranscode: {Dir} dizinine yazma izni yok. Sahiplik/izinleri kontrol edin. {Ex}", dir, ex.Message);
            return false;
        }
        catch (IOException ex)
        {
            _logger.LogError("PreTranscode: {Dir} dizinine yazilamadi. {Ex}", dir, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Ciktı dosyasinin izinlerini kaynak dosyayla esitler (Linux only).
    /// </summary>
    private void CopyPermissions(string sourcePath, string outputPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;

        try
        {
            var srcInfo = new FileInfo(sourcePath);
            var dstInfo = new FileInfo(outputPath);

            // Unix file mode'leri kopyala (.NET 9+ System.IO.UnixFileMode)
            try
            {
                var srcMode = srcInfo.UnixFileMode;
                if (srcMode != 0)
                {
                    dstInfo.UnixFileMode = srcMode;
                    _logger.LogDebug("PreTranscode: izinler kopyalandi {Mode:o} -> {Output}", srcMode, outputPath);
                }
            }
            catch (PlatformNotSupportedException)
            {
                // Eski .NET - chmod fallback
                RunChmod(sourcePath, outputPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PreTranscode: izin kopyalama atlandi");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PreTranscode: izin kopyalama hatasi");
        }
    }

    private void RunChmod(string sourcePath, string outputPath)
    {
        try
        {
            // stat ile kaynak izinleri al, chmod ile hedefe uygula
            var statPsi = new ProcessStartInfo
            {
                FileName = "stat",
                Arguments = $"-c '%a' \"{sourcePath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using var statProc = new Process { StartInfo = statPsi };
            statProc.Start();
            statProc.WaitForExit(5000);
            var mode = statProc.StandardOutput.ReadToEnd().Trim();

            if (!string.IsNullOrEmpty(mode))
            {
                var chmodPsi = new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"\"{mode}\" \"{outputPath}\"",
                    UseShellExecute = false
                };
                using var chmodProc = new Process { StartInfo = chmodPsi };
                chmodProc.Start();
                chmodProc.WaitForExit(5000);
            }
        }
        catch { /* best effort */ }
    }

    public async Task<bool> TranscodeAsync(string sourcePath, string outputPath, PluginConfiguration config, CancellationToken cancellationToken)
    {
        // 4a. Ciktı dizinine yazma izni kontrolu
        if (!CanWriteToDirectory(outputPath))
        {
            _logger.LogError("PreTranscode: {Output} dizinine yazma izni yok - is atlandi", outputPath);
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        // 3. Kaynak dosyanin bit derinligini probe et
        var pixFmt = GetSourcePixelFormat(sourcePath);
        _logger.LogDebug("PreTranscode: kaynak pix_fmt={PixFmt} dosya={Path}", pixFmt, sourcePath);

        var ffmpegPath = FfmpegPath;
        var args = BuildArguments(sourcePath, outputPath, config, pixFmt);
        _logger.LogInformation("PreTranscode: ffmpeg={Ffmpeg} args={Args}", ffmpegPath, args);

        var result = await RunFfmpeg(ffmpegPath, args, sourcePath, outputPath, cancellationToken);

        // HW encode basarisiz olduysa ve VAAPI/QSV kullaniliyorsa, software encode ile tekrar dene
        // AMF ve NVENC zaten codec'i destekliyor, fallback gerekmez
        if (!result && (config.HardwareAcceleration == HwAccelType.Vaapi || config.HardwareAcceleration == HwAccelType.Qsv))
        {
            _logger.LogWarning("PreTranscode: {Accel} encode basarisiz, software encode ile tekrar deneniyor", config.HardwareAcceleration);
            var fallbackConfig = new PluginConfiguration
            {
                HardwareAcceleration = HwAccelType.None,
                RenderDevicePath = config.RenderDevicePath,
                TargetVideoCodec = config.TargetVideoCodec,
                Quality = config.Quality,
                MaxWidth = config.MaxWidth,
                MinimumFileSizeMb = config.MinimumFileSizeMb,
                MaxConcurrentJobs = config.MaxConcurrentJobs,
                SkipIfAlreadyTargetCodec = config.SkipIfAlreadyTargetCodec,
                ReplaceOriginal = config.ReplaceOriginal,
                OutputDirectory = config.OutputDirectory
            };
            var fallbackArgs = BuildArguments(sourcePath, outputPath, fallbackConfig, pixFmt);
            _logger.LogInformation("PreTranscode: software fallback args={Args}", fallbackArgs);
            result = await RunFfmpeg(ffmpegPath, fallbackArgs, sourcePath, outputPath, cancellationToken);
        }

        return result;
    }

    private async Task<bool> RunFfmpeg(string ffmpegPath, string args, string sourcePath, string outputPath, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = args,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            _logger.LogError("PreTranscode: ffmpeg exited with code {Code} for {Path}. Stderr tail: {Stderr}",
                process.ExitCode, sourcePath, TailLines(stderr.ToString(), 30));

            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch (IOException) { }
            }

            return false;
        }

        // 4b. Izinleri kopyala
        CopyPermissions(sourcePath, outputPath);

        _logger.LogInformation("PreTranscode: finished {Source} -> {Output}", sourcePath, outputPath);
        return true;
    }

    private string BuildArguments(string sourcePath, string outputPath, PluginConfiguration config, string? sourcePixFmt)
    {
        var sb = new StringBuilder();
        sb.Append("-y -hide_banner -loglevel warning ");

        // 1. Donanim hizlandirma - VAAPI icin init_hw_device kullan
        switch (config.HardwareAcceleration)
        {
            case HwAccelType.Vaapi:
                // init_hw_device ile cihaz belirgin sekilde tanimla
                var vaPath = !string.IsNullOrEmpty(config.RenderDevicePath)
                    ? config.RenderDevicePath
                    : "/dev/dri/renderD128";
                sb.Append($"-init_hw_device vaapi=va:{vaPath} -filter_hw_device va ");
                sb.Append("-hwaccel vaapi -hwaccel_device va -hwaccel_output_format vaapi ");
                break;
            case HwAccelType.Qsv:
                sb.Append("-hwaccel qsv -hwaccel_output_format qsv ");
                break;
            case HwAccelType.Nvenc:
                sb.Append("-hwaccel cuda -hwaccel_device 0 -hwaccel_output_format cuda ");
                break;
            case HwAccelType.Amf:
                // AMF: CPU decode, GPU encode - hwaccel gerekmez
                break;
        }

        // Input path - Windows UNC icin file: prefix
        var inputPath = sourcePath.StartsWith("\\\\") || sourcePath.Contains(':')
            ? $"file:{sourcePath}"
            : sourcePath;

        sb.Append($"-i \"{inputPath}\" ");

        // 2. Stream mapping - sadece video, audio, subtitle (attachment hariç)
        sb.Append("-map 0:v:0 -map 0:a -map 0:s? ");

        // 3. Video filter - 10-bit kaynaklari 8-bit encoder'lara uyarla
        var videoFilters = new StringBuilder();

        if (config.HardwareAcceleration == HwAccelType.Vaapi)
        {
            // h264_vaapi sadece 8-bit destekler - 10-bit kaynaklari donustur
            var needsFormatConvert = Is10BitFormat(sourcePixFmt) && config.TargetVideoCodec == "h264";
            if (needsFormatConvert)
            {
                videoFilters.Append("format=nv12,");
                _logger.LogInformation("PreTranscode: 10-bit kaynak ({PixFmt}) -> nv12 format donusumu eklendi", sourcePixFmt);
            }

            // MaxWidth scale filtresi
            if (config.MaxWidth > 0)
            {
                videoFilters.Append($"scale_vaapi=w='min({config.MaxWidth},iw)':h=-2,");
            }
        }
        else if (config.HardwareAcceleration == HwAccelType.Nvenc)
        {
            // CUDA decode -> NVENC encode: hwdownload + format=nv12 gerekli
            videoFilters.Append("hwdownload,format=nv12,");
            if (config.MaxWidth > 0)
            {
                videoFilters.Append($"scale=w='min({config.MaxWidth},iw)':h=-2,");
            }
        }
        else if (config.HardwareAcceleration == HwAccelType.Amf)
        {
            // AMF: CPU decode -> GPU encode, 10-bit kaynaklari nv12'ye donustur
            if (Is10BitFormat(sourcePixFmt))
            {
                videoFilters.Append("format=nv12,");
            }
            if (config.MaxWidth > 0)
            {
                videoFilters.Append($"scale=w='min({config.MaxWidth},iw)':h=-2,");
            }
        }
        else if (config.HardwareAcceleration == HwAccelType.None)
        {
            // Yazılım encode - 10-bit kaynak, 8-bit hedef icin format donusumu
            var needsFormatConvert = Is10BitFormat(sourcePixFmt) && config.TargetVideoCodec == "h264";
            if (needsFormatConvert)
            {
                // HDR -> SDR tonemap + 10-bit -> 8-bit format donusumu
                videoFilters.Append("zscale=t=linear:npl=100,format=gbrpf32le,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p,");
                _logger.LogInformation("PreTranscode: HDR tonemap + 10-bit->8-bit donusum eklendi");
            }
            else if (Is10BitFormat(sourcePixFmt))
            {
                // 10-bit SDR -> hevc hedef
                if (config.MaxWidth > 0)
                {
                    videoFilters.Append($"scale=w='min({config.MaxWidth},iw)':h=-2,");
                }
            }
            else if (config.MaxWidth > 0)
            {
                videoFilters.Append($"scale=w='min({config.MaxWidth},iw)':h=-2,");
            }
        }

        // Filter stringi temizle ve ekle
        var vfStr = videoFilters.ToString();
        if (vfStr.EndsWith(",")) vfStr = vfStr[..^1];
        if (!string.IsNullOrEmpty(vfStr))
        {
            sb.Append($"-vf \"{vfStr}\" ");
        }

        // Video codec
        sb.Append(BuildVideoCodecArgs(config));
        sb.Append(" -c:a copy -c:s copy ");

        // Output path
        sb.Append($"\"{outputPath}\"");

        return sb.ToString();
    }

    /// <summary>
    /// pix_fmt'in 10-bit olup olmadigini kontrol eder.
    /// </summary>
    private static bool Is10BitFormat(string? pixFmt)
    {
        if (string.IsNullOrEmpty(pixFmt)) return false;
        var f = pixFmt.ToLowerInvariant();
        return f.Contains("p010") || f.Contains("10le") || f.Contains("10be")
            || f.Contains("yuv420p10") || f.Contains("yuv422p10") || f.Contains("yuv444p10")
            || f.Contains("gbrp10");
    }

    private string BuildVideoCodecArgs(PluginConfiguration config)
    {
        var codecSuffix = config.HardwareAcceleration switch
        {
            HwAccelType.Vaapi => "_vaapi",
            HwAccelType.Qsv => "_qsv",
            HwAccelType.Nvenc => "_nvenc",
            HwAccelType.Amf => "_amf",
            _ => string.Empty
        };

        var baseCodec = config.TargetVideoCodec == "h264" ? "h264" : "hevc";
        var encoder = baseCodec + codecSuffix;

        return config.HardwareAcceleration switch
        {
            HwAccelType.None => $"-c:v lib{(baseCodec == "hevc" ? "x265" : "x264")} -preset medium -crf {config.Quality}",
            HwAccelType.Amf => $"-c:v {encoder} -rc_mode vq -qp {config.Quality}",
            _ => $"-c:v {encoder} -qp {config.Quality}"
        };
    }

    private static string TailLines(string text, int count)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var start = Math.Max(0, lines.Length - count);
        return string.Join('\n', lines[start..]);
    }
}
