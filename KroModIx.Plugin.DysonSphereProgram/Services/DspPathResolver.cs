using System.IO;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.DysonSphereProgram.Services;

/// <summary>Findet den BepInEx-Plugins-Ordner unter <c>&lt;InstallDir&gt;/BepInEx/plugins/</c>.
/// BepInEx wird vom User via Nexus-Mod „BepInEx Pack for Dyson Sphere Program"
/// installiert (entpackt sich ins Game-Root), das Plugin greift danach.</summary>
public sealed class DspPathResolver
{
    /// <summary>Absoluter Pfad zum BepInEx-Plugins-Ordner. Rueckgabe garantiert
    /// nicht dass er existiert — <see cref="LooksLikeBepInExInstall"/> davor rufen.</summary>
    public string GetPluginsDir(DetectedGame game) =>
        Path.Combine(game.InstallDir, "BepInEx", "plugins");

    /// <summary>BepInEx-Marker: <c>BepInEx/core/BepInEx.dll</c> muss existieren.
    /// Ohne diesen Marker ist nur das reine Nexus-Zip entpackt (Nutzer hat's
    /// noch nicht gestartet) oder BepInEx wurde nie installiert.</summary>
    public bool LooksLikeBepInExInstall(DetectedGame game)
    {
        if (string.IsNullOrEmpty(game.InstallDir)) return false;
        return File.Exists(Path.Combine(game.InstallDir, "BepInEx", "core", "BepInEx.dll"));
    }
}
