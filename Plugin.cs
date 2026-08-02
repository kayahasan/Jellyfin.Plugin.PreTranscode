using System;
using System.Collections.Generic;
using Jellyfin.Plugin.PreTranscode.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Controller.Activity;

namespace Jellyfin.Plugin.PreTranscode;

/// <summary>
/// PreTranscode Jellyfin Plugin.
/// NOT: Windows'ta uninstall icin Jellyfin'i durdurup
/// C:\ProgramData\Jellyfin\Server\plugins\Jellyfin.Plugin.PreTranscode klasorunu manuel silin.
/// DLL process tarafindan kullanildigi icin otomatik silme calismaz.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IDisposable
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override string Name => "PreTranscode";

    public override Guid Id => Guid.Parse("b3a1c9e4-6f2d-4a8b-9c3e-2d1f5a7b8c90");

    public override string Description =>
        "Kutuphanenizdeki uyumsuz video dosyalarini otomatik olarak tarar, secerek veya " +
        "zamanli gorev ile yeniden kodlar. VAAPI, Intel QSV ve NVIDIA NVENC donanim hizlandirmasini " +
        "destekler. Film ve dizi dosyalarinizi hedef codec'e (HEVC/H.264) donusturur, " +
        "depolama alanini tasarruf eder ve uyumlulugu artirir.\n\n" +
        "Automatically scans your library for non-standard video files and re-encodes them " +
        "on-demand or via scheduled tasks. Supports VAAPI, Intel QSV, and NVIDIA NVENC hardware " +
        "acceleration. Transcodes movie and series content to your target codec (HEVC/H.264), " +
        "saving storage space and improving compatibility.";

    public static Plugin? Instance { get; private set; }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "PreTranscode",
                DisplayName = "PreTranscode",
                EnableInMainMenu = true,
                MenuIcon = "movie",
                EmbeddedResourcePath = string.Format("{0}.Configuration.list.html", GetType().Namespace)
            }
        };
    }

    public void Dispose()
    {
        Instance = null;
    }
}
