using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.DysonSphereProgram.Services;

/// <summary>Laedt BepInEx direkt vom BepInEx-GitHub-Release und entpackt
/// es ins Game-Root. Ohne diesen Bootstrap-Service muesste der User manuell
/// von Nexus (mods/13) oder BepInEx-Github laden, entpacken und ins Game
/// legen — genau der Reibungspunkt den ein Modmanager wegnehmen soll
/// (Skill Kernprinzip 6).
///
/// <para><b>DSP nutzt Unity Mono (2018-Generation), nicht IL2CPP</b> —
/// braucht daher <c>BepInEx v5.x stable</c> (Mono-Variante), NICHT die
/// v6-pre-IL2CPP-Variante. Asset-Pattern: <c>BepInEx_win_x64_{ver}.zip</c>
/// (Underscore-Naming; nur v6-preX nutzt den Bindestrich-Namensraum
/// <c>BepInEx-Unity.IL2CPP-win-x64-*</c>).</para>
///
/// <para><b>Seit v0.9.0 über <c>IHostServices.GitHub</c> und
/// <c>.Archives</c></b> (Host v1.33.0), und <b>die fest hinterlegte
/// Ausweich-URL ist weg.</b> Sie zeigte auf <c>v5.4.23.5</c> und wäre mit
/// jeder neuen BepInEx-Ausgabe weiter veraltet — ein Nutzer, der beim
/// GitHub-Limit landet, hätte stillschweigend eine alte Fassung bekommen,
/// ohne es zu erfahren.</para>
///
/// <para><b>Der Dateiname trägt hier die Version</b>, anders als bei
/// MelonLoader. Das ist kein Hindernis: der Baukasten liefert neben dem Tag
/// (<c>v5.4.23.5</c>) auch die Fassung ohne <c>v</c>
/// (<c>5.4.23.5</c>) — und genau die steht im Dateinamen. Der
/// Umleitungs-Pfad kommt damit ohne API-Aufruf zur richtigen URL.</para></summary>
public sealed class BepInExBootstrapper
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private const string Repo = "BepInEx/BepInEx";

    /// <summary>Die Mono-Fassung fuer Windows x64. Der Vergleich laeuft
    /// ueber Anfang <b>und</b> Ende, damit die v6-IL2CPP-Vorabausgaben
    /// (<c>BepInEx-Unity.IL2CPP-win-x64-*</c>) nicht mitgenommen werden —
    /// DSP ist Unity Mono.</summary>
    private const string AssetPrefix = "BepInEx_win_x64_";

    private readonly HttpClient _http;
    private readonly IGitHubService _gitHub;
    private readonly IArchiveService _archives;

    public BepInExBootstrapper(HttpClient http, IGitHubService gitHub, IArchiveService archives)
    {
        _http = http;
        _gitHub = gitHub;
        _archives = archives;
    }

    /// <summary>Downloadet + entpackt BepInEx (Mono, win-x64) ins
    /// <paramref name="installDir"/>. Bricht bei jedem Fehler mit einer
    /// Message ab die dem User sagt was schiefging.</summary>
    public async Task<BepInExInstallResult> InstallAsync(string installDir,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            progress?.Report(0.05);

            var (url, version) = await ResolveAssetAsync(ct).ConfigureAwait(false);
            if (url is null)
                return BepInExInstallResult.Fail(
                    "Kein BepInEx-Release gefunden — weder über die GitHub-API noch über den "
                    + "Umleitungs-Pfad. Besteht eine Internetverbindung?");

            Log.Info("BepInEx-Download: {Ver} von {Url}", version, url);
            progress?.Report(0.1);

            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            var tmp = Path.Combine(Path.GetTempPath(), $"bepinex-dsp-{Guid.NewGuid():N}.zip");
            try
            {
                long total = resp.Content.Headers.ContentLength ?? 0;
                await using (var input = await resp.Content.ReadAsStreamAsync(ct))
                await using (var output = File.Create(tmp))
                {
                    var buf = new byte[81920];
                    long done = 0;
                    int n;
                    while ((n = await input.ReadAsync(buf, ct)) > 0)
                    {
                        await output.WriteAsync(buf.AsMemory(0, n), ct);
                        done += n;
                        if (total > 0 && progress is not null)
                            progress.Report(0.1 + (double)done / total * 0.7);
                    }
                }
                progress?.Report(0.85);

                // Ins Game-Root extrahieren — BepInEx-Zip enthaelt bereits
                // BepInEx/, dotnet/, winhttp.dll, doorstop_config.ini auf Root-Ebene.
                var r = await Task.Run(() => _archives.Extract(tmp, installDir), ct)
                    .ConfigureAwait(false);
                if (r.SkippedUnsafe.Count > 0)
                {
                    // Bei BepInEx selbst waere das ein Alarmzeichen: das ist
                    // ein Release eines bekannten Projekts, nicht ein
                    // Nutzer-Archiv. Lieber abbrechen und melden.
                    Log.Warn("BepInEx-Archiv enthielt {Count} Ausbruchsversuch(e): {Entries}",
                        r.SkippedUnsafe.Count, string.Join(", ", r.SkippedUnsafe));
                    return BepInExInstallResult.Fail(
                        $"Das BepInEx-Archiv enthielt {r.SkippedUnsafe.Count} Eintrag/Einträge, "
                        + "die aus dem Spielverzeichnis herausschreiben wollten. Abgebrochen — "
                        + "das sollte bei einem offiziellen Release nicht vorkommen.");
                }

                progress?.Report(1.0);
                Log.Info("BepInEx {Ver} installiert nach {Dir} ({N} Datei(en))",
                    version, installDir, r.Count);
                return BepInExInstallResult.Ok(version ?? "unbekannt");
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* Aufräumen darf scheitern */ }
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "BepInEx-Install fehlgeschlagen");
            return BepInExInstallResult.Fail(ex.Message);
        }
    }

    /// <summary>Erst die Dateiliste der neuesten stabilen Ausgabe; greift die
    /// Raten-Sperre, kennt der Baukasten nur den Tag — dann wird der
    /// Dateiname aus der Fassung gebildet
    /// (<c>BepInEx_win_x64_5.4.23.5.zip</c> zum Tag <c>v5.4.23.5</c>).</summary>
    private async Task<(string? Url, string? Version)> ResolveAssetAsync(CancellationToken ct)
    {
        var hit = await _gitHub.FindLatestAssetAsync(Repo, PasstAufDieMonoFassung, ct)
            .ConfigureAwait(false);
        if (hit is not null)
            return (hit.Value.Asset.DownloadUrl, hit.Value.Release.Tag);

        var release = await _gitHub.GetLatestReleaseAsync(Repo, ct).ConfigureAwait(false);
        if (release is null) return (null, null);

        var name = $"{AssetPrefix}{release.Version}.zip";
        Log.Info("Dateiliste nicht abrufbar ({Grund}) — URL aus Tag {Tag} und Namenskonvention {Name}",
            _gitHub.IsRateLimited ? "GitHub-Limit erreicht" : "keine passende Datei gemeldet",
            release.Tag, name);
        return (_gitHub.BuildAssetUrl(Repo, release.Tag, name), release.Tag);
    }

    private static bool PasstAufDieMonoFassung(string assetName)
        => assetName.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase)
           && assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
}

public sealed record BepInExInstallResult(bool Success, string? Version, string? ErrorMessage)
{
    public static BepInExInstallResult Ok(string version) => new(true, version, null);
    public static BepInExInstallResult Fail(string message) => new(false, null, message);
}
