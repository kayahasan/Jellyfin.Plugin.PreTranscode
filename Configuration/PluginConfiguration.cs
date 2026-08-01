using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PreTranscode.Configuration;

public enum HwAccelType
{
    None,
    Vaapi,
    Qsv,
    Nvenc,
    Amf
}

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Kod dönüştürme için kullanılacak donanım hızlandırma tipi.
    /// </summary>
    public HwAccelType HardwareAcceleration { get; set; } = HwAccelType.Vaapi;

    /// <summary>
    /// VAAPI/render cihazı yolu, örn. /dev/dri/renderD128
    /// </summary>
    public string RenderDevicePath { get; set; } = "/dev/dri/renderD128";

    /// <summary>
    /// Hedef video codec: h264, hevc
    /// </summary>
    public string TargetVideoCodec { get; set; } = "hevc";

    /// <summary>
    /// CRF/QP değeri (yazılım fallback için) - donanım encoder'da rc_mode'a çevrilir.
    /// </summary>
    public int Quality { get; set; } = 24;

    /// <summary>
    /// Hedef maksimum genişlik (0 = orijinal çözünürlüğü koru).
    /// </summary>
    public int MaxWidth { get; set; } = 1920;

    /// <summary>
    /// Zaten bu codec'te olan dosyaları atla.
    /// </summary>
    public bool SkipIfAlreadyTargetCodec { get; set; } = true;

    /// <summary>
    /// Bu boyuttan (MB) küçük dosyaları atla - küçük dosyalarda kazanç azdır.
    /// </summary>
    public int MinimumFileSizeMb { get; set; } = 500;

    /// <summary>
    /// Aynı anda kaç dosya işlensin.
    /// </summary>
    public int MaxConcurrentJobs { get; set; } = 1;

    /// <summary>
    /// Üretilen dosyaların yazılacağı klasör. Boşsa orijinalin yanına "-optimized" olarak yazılır.
    /// </summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Orijinali silip yenisiyle değiştirme (tehlikeli). False ise alternate version olarak eklenir.
    /// </summary>
    public bool ReplaceOriginal { get; set; } = false;
}
