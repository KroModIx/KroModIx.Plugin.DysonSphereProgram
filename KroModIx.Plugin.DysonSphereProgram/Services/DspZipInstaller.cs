using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KroModIx.Plugin.Contracts;
using NLog;
using SharpCompress.Archives;

namespace KroModIx.Plugin.DysonSphereProgram.Services;

/// <summary>Installiert ein Nexus-Mod-Archiv (ZIP/RAR/7z) unter
/// <c>BepInEx/plugins/</c>. Auto-Layout-Detection:
/// <list type="bullet">
/// <item>Archive enthaelt schon <c>BepInEx/plugins/&lt;Name&gt;/</c> → direktes
/// Extract ins Game-Root (behaelt Ordner-Struktur).</item>
/// <item>Archive enthaelt direkt eine .dll auf Root-Ebene → nach
/// <c>BepInEx/plugins/</c> extrahieren.</item>
/// <item>Archive enthaelt einen Root-Ordner mit einer .dll drin (typisches
/// Nexus-Layout „&lt;ModName&gt;/&lt;ModName&gt;.dll") → als Ordner-Plugin nach
/// <c>BepInEx/plugins/</c> extrahieren.</item>
/// </list>
/// </summary>
public sealed class DspZipInstaller
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly DspInstallManifestStore? _manifests;

    public DspZipInstaller(DspInstallManifestStore? manifests = null)
    {
        _manifests = manifests;
    }

    public DspZipInstallResult Install(string archivePath, DetectedGame game)
    {
        if (!File.Exists(archivePath))
            return DspZipInstallResult.Fail($"Archiv nicht gefunden: {archivePath}");
        var installDir = game.InstallDir;
        if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir))
            return DspZipInstallResult.Fail($"DSP-InstallDir ungueltig: {installDir}");

        try
        {
            using var archive = ArchiveFactory.Open(archivePath);
            var entries = archive.Entries
                .Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key))
                .ToList();
            if (entries.Count == 0)
                return DspZipInstallResult.Fail("Archiv ist leer.");

            var normalized = entries.Select(e => (e.Key ?? "").Replace('\\', '/')).ToList();

            // 1) Bekanntes Layout — enthaelt BepInEx/plugins/ (oder BepInEx/core/…)
            bool knownLayout = normalized.Any(p =>
                p.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase));
            if (knownLayout)
            {
                var installed = ExtractDirect(entries, installDir);
                WriteManifests(installed, installDir, archivePath);
                return DspZipInstallResult.Ok(
                    $"Direkt-Layout: {installed.Count} Datei(en) ins Game-Root extrahiert.", installed);
            }

            // 2) Flat DLL(s) auf Archive-Root → nach BepInEx/plugins/
            var pluginsDir = Path.Combine(installDir, "BepInEx", "plugins");
            Directory.CreateDirectory(pluginsDir);
            var rootDlls = entries.Where(e =>
                (e.Key ?? "").IndexOf('/') < 0
                && (e.Key ?? "").EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();
            if (rootDlls.Count > 0)
            {
                var installedFlat = new List<string>();
                foreach (var e in rootDlls)
                {
                    var name = Path.GetFileName(e.Key!);
                    var dst = Path.Combine(pluginsDir, name);
                    ExtractOne(e, dst);
                    installedFlat.Add(dst);
                }
                WriteManifests(installedFlat, installDir, archivePath);
                return DspZipInstallResult.Ok(
                    $"Flat-Layout: {installedFlat.Count} DLL(s) nach BepInEx/plugins/ extrahiert.",
                    installedFlat);
            }

            // 3) Ordner-Layout: einziger Root-Ordner enthaelt DLL(s)
            var rootDirs = normalized
                .Where(p => p.IndexOf('/') > 0)
                .Select(p => p.Split('/')[0])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (rootDirs.Count == 1)
            {
                var rootName = rootDirs[0];
                var targetFolder = Path.Combine(pluginsDir, rootName);
                Directory.CreateDirectory(targetFolder);
                var installedFolder = new List<string>();
                foreach (var e in entries)
                {
                    var relInArchive = (e.Key ?? "").Replace('\\', '/');
                    // Root-Ordner-Prefix strippen
                    var rel = relInArchive.Substring(rootName.Length + 1);
                    var dst = Path.Combine(targetFolder, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    ExtractOne(e, dst);
                    installedFolder.Add(dst);
                }
                WriteManifests(installedFolder, installDir, archivePath, rootName);
                return DspZipInstallResult.Ok(
                    $"Ordner-Layout '{rootName}': {installedFolder.Count} Datei(en) nach BepInEx/plugins/{rootName}/ extrahiert.",
                    installedFolder);
            }

            return DspZipInstallResult.Fail(
                "Unbekanntes Archiv-Layout. Bitte manuell nach BepInEx/plugins/ entpacken. " +
                $"Enthaelt {entries.Count} Datei(en) in Ordnern: " +
                string.Join(", ", normalized.Take(5).Select(p => Path.GetDirectoryName(p)).Distinct()));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Install fehlgeschlagen: {Archive}", archivePath);
            return DspZipInstallResult.Fail($"Fehler: {ex.Message}");
        }
    }

    private static IReadOnlyList<string> ExtractDirect(IEnumerable<IArchiveEntry> entries, string installDir)
    {
        var installed = new List<string>();
        foreach (var e in entries)
        {
            var name = (e.Key ?? "").Replace('\\', '/');
            if (string.IsNullOrEmpty(name) || name.EndsWith('/')) continue;
            if (name.Contains("..")) { Log.Warn("Zip-Slip: {N}", name); continue; }
            var dst = Path.Combine(installDir, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            ExtractOne(e, dst);
            installed.Add(dst);
        }
        return installed;
    }

    private static void ExtractOne(IArchiveEntry entry, string destination)
    {
        using var input = entry.OpenEntryStream();
        using var output = File.Create(destination);
        input.CopyTo(output);
    }

    /// <summary>Fuer jeden installierten DLL-Namen ein Manifest im
    /// <see cref="DspInstallManifestStore"/> speichern. ModId + Version
    /// aus dem Nexus-CDN-Filename (falls Nexus-Naming) — sonst leer,
    /// dann kein Update-Discovery moeglich fuer diesen Mod.</summary>
    private void WriteManifests(IReadOnlyList<string> installedPaths, string installDir,
        string archivePath, string? explicitModName = null)
    {
        if (_manifests is null) return;
        var archiveName = Path.GetFileName(archivePath);
        var nexusModId = NexusFileNameParser.TryExtractModId(archiveName);
        var nexusVersion = NexusFileNameParser.TryExtractVersion(archiveName);

        // Explizit uebergebener Ordner-Name (Ordner-Layout) → ein Manifest pro Ordner.
        // Sonst: pro installierter .dll ein Manifest.
        var manifestNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(explicitModName))
        {
            manifestNames.Add(explicitModName);
        }
        else
        {
            foreach (var p in installedPaths)
            {
                if (p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    manifestNames.Add(Path.GetFileNameWithoutExtension(p));
            }
        }
        foreach (var name in manifestNames)
        {
            var key = DspInstallManifestStore.BuildKey(name);
            _manifests.Save(key, new DspInstallManifest(
                NexusModId: nexusModId,
                OriginalFilename: archiveName,
                NexusVersion: nexusVersion,
                InstalledAtUtc: DateTime.UtcNow));
        }
    }

    public static readonly string[] SupportedExtensions = new[] { ".zip", ".rar", ".7z" };
}

public sealed record DspZipInstallResult(bool Success, string Message, IReadOnlyList<string> InstalledPaths)
{
    public static DspZipInstallResult Ok(string msg, IReadOnlyList<string> paths) => new(true, msg, paths);
    public static DspZipInstallResult Fail(string msg) => new(false, msg, Array.Empty<string>());
}
