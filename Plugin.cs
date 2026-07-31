using System;
using System.Collections.Generic;
using Jellyfin.Plugin.PreTranscode.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.PreTranscode;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override string Name => "PreTranscode";

    public override Guid Id => Guid.Parse("b3a1c9e4-6f2d-4a8b-9c3e-2d1f5a7b8c90");

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
}
