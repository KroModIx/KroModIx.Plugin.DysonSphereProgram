using System.IO;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.DysonSphereProgram.Services;

/// <summary>Plugin-eigene Pfade (Downloads-Cache, Cover-Cache). Nutzt die
/// vom Host bereitgestellten Data-/Cache-Roots — ~/.config/KroModIx/plugin-data/
/// und ~/.cache/KroModIx/plugin-cache/ pro Plugin.</summary>
public sealed class DspPaths
{
    private readonly IHostServices _host;

    public DspPaths(IHostServices host)
    {
        _host = host;
        Directory.CreateDirectory(DownloadsDir);
        Directory.CreateDirectory(CoverCacheDir);
    }

    public string DownloadsDir => Path.Combine(_host.PluginDataDir, "downloads");
    public string CoverCacheDir => Path.Combine(_host.PluginCacheDir, "covers");
}
