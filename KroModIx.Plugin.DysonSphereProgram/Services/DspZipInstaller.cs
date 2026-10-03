using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KroModIx.Plugin.Contracts;
using NLog;

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
    private readonly IArchiveService _archives;
    private readonly DspInstallManifestStore? _manifests;

    public DspZipInstaller(IArchiveService archives, DspInstallManifestStore? manifests = null)
    {
        _archives = archives;
        _manifests = manifests;
    }

    /// <summary>Endungs-Vorfilter fuer den Downloads-Tab. Kommt aus dem
    /// Host-Baukasten, damit ein dort neu unterstuetztes Format nicht in
    /// neun Plugins nachgetragen werden muss.</summary>
    public IReadOnlyList<string> SupportedExtensions => _archives.SupportedExtensions;

    public bool HasSupportedExtension(string path) => _archives.HasSupportedExtension(path);

    public DspZipInstallResult Install(string archivePath, DetectedGame game)
    {
        if (!File.Exists(archivePath))
            return DspZipInstallResult.Fail($"Archiv nicht gefunden: {archivePath}");
        var installDir = game.InstallDir;
        if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir))
            return DspZipInstallResult.Fail($"DSP-InstallDir ungueltig: {installDir}");

        try
        {
            // Am Inhalt pruefen, nicht an der Endung: ein Download mit
            // falscher Endung landete sonst unveraendert im Spiel.
            if (_archives.DetectKind(archivePath) == ArchiveKind.Unknown)
                return DspZipInstallResult.Fail(
                    "Das ist kein lesbares Archiv (ZIP/RAR/7z) — eventuell ein abgebrochener Download.");

            var entries = _archives.List(archivePath);
            if (entries.Count == 0)
                return DspZipInstallResult.Fail("Archiv ist leer.");

            // 1) Bekanntes Layout — enthaelt BepInEx/plugins/ (oder BepInEx/core/…)
            bool knownLayout = entries.Any(e =>
                e.Path.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase));
            if (knownLayout)
            {
                var r = _archives.Extract(archivePath, installDir);
                if (Abgelehnt(r) is { } warnung) return DspZipInstallResult.Fail(warnung);
                WriteManifests(r.ExtractedPaths, installDir, archivePath);
                return DspZipInstallResult.Ok(
                    $"Direkt-Layout: {r.Count} Datei(en) ins Game-Root extrahiert.",
                    r.ExtractedPaths);
            }

            // 2) Flat DLL(s) auf Archive-Root → nach BepInEx/plugins/
            // v0.8.0: anlegen statt annehmen — BepInEx legt plugins/ erst beim
            // ersten Spielstart an.
            var pluginsDir = ModFolderDiscovery.FindOrCreate(installDir, "BepInEx/plugins")
                             ?? Path.Combine(installDir, "BepInEx", "plugins");
            Directory.CreateDirectory(pluginsDir);
            bool hatWurzelDlls = entries.Any(e =>
                e.Path.IndexOf('/') < 0
                && e.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            if (hatWurzelDlls)
            {
                var r = _archives.Extract(archivePath, pluginsDir, new ArchiveExtractOptions(
                    Filter: p => p.IndexOf('/') < 0
                                 && p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)));
                if (Abgelehnt(r) is { } warnung) return DspZipInstallResult.Fail(warnung);
                WriteManifests(r.ExtractedPaths, installDir, archivePath);
                return DspZipInstallResult.Ok(
                    $"Flat-Layout: {r.Count} DLL(s) nach BepInEx/plugins/ extrahiert.",
                    r.ExtractedPaths);
            }

            // 3) Ordner-Layout: einziger Root-Ordner enthaelt DLL(s)
            var rootDirs = entries
                .Where(e => e.Path.IndexOf('/') > 0)
                .Select(e => e.Path.Split('/')[0])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (rootDirs.Count == 1)
            {
                var rootName = rootDirs[0];
                // Der Ordnername kommt aus dem Archiv, ist also genauso
                // unvertrauenswuerdig wie ein Eintragspfad — derselbe
                // Schutz, hier noch vor dem Anlegen.
                if (!_archives.TryResolveSafe(pluginsDir, rootName, out var targetFolder))
                {
                    Log.Warn("Zip-Slip im Root-Ordnernamen: {N}", rootName);
                    return DspZipInstallResult.Fail($"Unsicherer Ordnername im Archiv: {rootName}");
                }
                Directory.CreateDirectory(targetFolder);
                // Eintraege ausserhalb des Root-Ordners (ein README daneben)
                // ueberspringt StripPrefix von sich aus — frueher lief das in
                // eine ArgumentOutOfRangeException im Substring.
                var r = _archives.Extract(archivePath, targetFolder,
                    new ArchiveExtractOptions(StripPrefix: rootName));
                if (Abgelehnt(r) is { } warnung) return DspZipInstallResult.Fail(warnung);
                WriteManifests(r.ExtractedPaths, installDir, archivePath, rootName);
                return DspZipInstallResult.Ok(
                    $"Ordner-Layout '{rootName}': {r.Count} Datei(en) nach BepInEx/plugins/{rootName}/ extrahiert.",
                    r.ExtractedPaths);
            }

            return DspZipInstallResult.Fail(
                "Unbekanntes Archiv-Layout. Bitte manuell nach BepInEx/plugins/ entpacken. " +
                $"Enthaelt {entries.Count} Datei(en) in Ordnern: " +
                string.Join(", ", entries.Take(5).Select(e => Path.GetDirectoryName(e.Path)).Distinct()));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Install fehlgeschlagen: {Archive}", archivePath);
            return DspZipInstallResult.Fail($"Fehler: {ex.Message}");
        }
    }

    /// <summary>Hat der Ausbruch-Schutz Eintraege abgelehnt, bricht der
    /// Install mit Meldung ab.
    ///
    /// <para><b>Das ist die Aenderung gegenueber v0.8.0.</b> Der eigene
    /// Schutz war inhaltlich richtig — er loeste jeden Pfad gegen das Ziel
    /// auf —, aber er <b>uebersprang</b> den abgelehnten Eintrag still und
    /// meldete am Ende Erfolg. Wer ein Archiv mit einem Ausbruchsversuch
    /// installierte, sah „Direkt-Layout: 42 Datei(en)" und erfuhr nichts
    /// von der dreiundvierzigsten.</para></summary>
    private static string? Abgelehnt(ArchiveExtractResult r)
    {
        if (r.SkippedUnsafe.Count == 0) return null;
        Log.Warn("Ausbruchsversuch im Archiv, {Count} Eintrag/Eintraege abgelehnt: {Entries}",
            r.SkippedUnsafe.Count, string.Join(", ", r.SkippedUnsafe));
        return $"Abgebrochen: {r.SkippedUnsafe.Count} Eintrag/Eintraege wollten aus dem "
             + "Spielverzeichnis herausschreiben — "
             + string.Join(", ", r.SkippedUnsafe.Take(3))
             + (r.SkippedUnsafe.Count > 3 ? ", …" : "")
             + $". {r.Count} Datei(en) waren schon geschrieben, bevor das auffiel.";
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

}

public sealed record DspZipInstallResult(bool Success, string Message, IReadOnlyList<string> InstalledPaths)
{
    public static DspZipInstallResult Ok(string msg, IReadOnlyList<string> paths) => new(true, msg, paths);
    public static DspZipInstallResult Fail(string msg) => new(false, msg, Array.Empty<string>());
}
