using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.DysonSphereProgram.Services;

/// <summary>Enumeriert alle BepInEx-Plugins unter <c>BepInEx/plugins/</c>.
/// Zwei Layouts sind erlaubt (beide von BepInEx unterstuetzt):
/// <list type="bullet">
/// <item>Flat: <c>plugins/&lt;Name&gt;.dll</c></item>
/// <item>Ordner: <c>plugins/&lt;Name&gt;/&lt;Name&gt;.dll</c> (+ config, README, …)</item>
/// </list>
/// Toggle via <c>.disabled</c>-Suffix — BepInEx laedt nur Extension .dll.</summary>
public sealed class BepInExScanner
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly DspPathResolver _paths;

    public BepInExScanner(DspPathResolver paths) => _paths = paths;

    public IReadOnlyList<DspMod> ScanAll(DetectedGame game)
    {
        var dir = _paths.GetPluginsDir(game);
        if (!Directory.Exists(dir))
        {
            Log.Debug("BepInEx/plugins existiert nicht: {Dir}", dir);
            return Array.Empty<DspMod>();
        }

        var result = new List<DspMod>();
        // 1) Flat: alle .dll und .dll.disabled im plugins-Root
        foreach (var f in EnumerateSafe(dir, "*.dll*"))
        {
            var name = Path.GetFileName(f);
            var (baseName, enabled) = ClassifyDllName(name);
            if (baseName is null) continue;
            var info = new FileInfo(f);
            result.Add(new DspMod(
                Path: f,
                Name: baseName,
                IsEnabled: enabled,
                IsDirectory: false,
                SizeBytes: info.Length,
                InstalledUtc: info.LastWriteTimeUtc));
        }

        // 2) Ordner-Layout: pro Unterordner den Haupt-DLL suchen
        foreach (var subdir in EnumerateDirs(dir))
        {
            var subInfo = new DirectoryInfo(subdir);
            var name = subInfo.Name;
            var enabled = !name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var displayName = enabled ? name : name[..^".disabled".Length];
            long size = 0;
            try { size = subInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
            catch { }
            result.Add(new DspMod(
                Path: subdir,
                Name: displayName,
                IsEnabled: enabled,
                IsDirectory: true,
                SizeBytes: size,
                InstalledUtc: subInfo.LastWriteTimeUtc));
        }

        return result.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Klassifiziert einen Dateinamen im plugins-Root:
    /// <c>Foo.dll</c> → (Foo, true), <c>Foo.dll.disabled</c> → (Foo, false),
    /// alles andere → (null, _).</summary>
    private static (string? BaseName, bool Enabled) ClassifyDllName(string filename)
    {
        if (filename.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return (filename[..^".dll".Length], true);
        if (filename.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase))
            return (filename[..^".dll.disabled".Length], false);
        return (null, false);
    }

    private static IEnumerable<string> EnumerateSafe(string dir, string pattern)
    {
        try { return Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> EnumerateDirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir); }
        catch { return Array.Empty<string>(); }
    }
}
