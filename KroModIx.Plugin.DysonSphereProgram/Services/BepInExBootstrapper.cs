using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
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
/// <c>BepInEx-Unity.IL2CPP-win-x64-*</c>).</para></summary>
public sealed class BepInExBootstrapper
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private const string ReleasesApi = "https://api.github.com/repos/BepInEx/BepInEx/releases";

    private readonly HttpClient _http;

    public BepInExBootstrapper(HttpClient http) => _http = http;

    /// <summary>Downloadet + entpackt BepInEx IL2CPP x64 (bleeding-edge oder
    /// latest-stable) ins <paramref name="installDir"/>. Bricht bei jedem
    /// Fehler mit einer Message ab die dem User sagt was schiefging.</summary>
    public async Task<BepInExInstallResult> InstallAsync(string installDir,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            progress?.Report(0.05);
            // Release-Assets fetchen (bis zu 30 Releases nach neuesten sortiert).
            _http.DefaultRequestHeaders.UserAgent.TryParseAdd("KroModIx-DSP-Plugin/1.0");
            _http.DefaultRequestHeaders.Accept.TryParseAdd("application/vnd.github+json");
            var releasesJson = await _http.GetStringAsync(ReleasesApi + "?per_page=30", ct);
            var releases = JsonSerializer.Deserialize<GhRelease[]>(releasesJson, JsonOpts);
            if (releases is null || releases.Length == 0)
                return BepInExInstallResult.Fail("BepInEx-Releases-Liste ist leer.");

            // Zielt: BepInEx v5.x stable Mono win_x64 (DSP ist Unity Mono).
            // Linux nutzt Proton → braucht die Windows-x64-Variante (Proton
            // uebersetzt Windows-DLL-Calls, Mono-Loader funktioniert transparent).
            // Prerelease-Releases (v6-pre) werden uebersprungen — die sind
            // IL2CPP-focused und wuerden auf einem Mono-Spiel nicht laden.
            string? url = null; string? assetName = null; string? version = null;
            foreach (var rel in releases)
            {
                if (rel.Prerelease) continue; // v6 pre skippen
                foreach (var asset in rel.Assets ?? Array.Empty<GhAsset>())
                {
                    var name = asset.Name ?? "";
                    // BepInEx_win_x64_<ver>.zip (v5-Mono-Naming, Underscore)
                    if (name.StartsWith("BepInEx_win_x64_", StringComparison.OrdinalIgnoreCase)
                        && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        url = asset.BrowserDownloadUrl;
                        assetName = name;
                        version = rel.TagName;
                        break;
                    }
                }
                if (url is not null) break;
            }
            if (url is null)
                return BepInExInstallResult.Fail(
                    "Kein passendes BepInEx_win_x64-ZIP (v5 Mono, stable) in den letzten 30 " +
                    "BepInEx-Releases gefunden. Fallback: manuell von github.com/BepInEx/BepInEx/releases " +
                    "laden und ins Game-Root entpacken.");

            Log.Info("BepInEx-Download: {Asset} von {Url}", assetName, url);
            progress?.Report(0.1);

            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            var tmp = Path.Combine(Path.GetTempPath(),
                $"bepinex-dsp-{Guid.NewGuid():N}.zip");
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
                await Task.Run(() =>
                {
                    using var zip = ZipFile.OpenRead(tmp);
                    foreach (var entry in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue; // Directory-Marker
                        var target = Path.Combine(installDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                        // Zip-Slip-Prevention
                        var full = Path.GetFullPath(target);
                        if (!full.StartsWith(Path.GetFullPath(installDir), StringComparison.OrdinalIgnoreCase))
                        {
                            Log.Warn("Zip-Slip-Attempt: {Entry}", entry.FullName);
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        entry.ExtractToFile(target, overwrite: true);
                    }
                }, ct);
                progress?.Report(1.0);
                Log.Info("BepInEx {Ver} installiert nach {Dir}", version, installDir);
                return BepInExInstallResult.Ok(version ?? "unbekannt");
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "BepInEx-Install fehlgeschlagen");
            return BepInExInstallResult.Fail(ex.Message);
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class GhRelease
    {
        public string? TagName { get; set; }
        public bool Prerelease { get; set; }
        public GhAsset[]? Assets { get; set; }
    }
    private sealed class GhAsset
    {
        public string? Name { get; set; }
        public string? BrowserDownloadUrl { get; set; }
    }
}

public sealed record BepInExInstallResult(bool Success, string? Version, string? ErrorMessage)
{
    public static BepInExInstallResult Ok(string version) => new(true, version, null);
    public static BepInExInstallResult Fail(string message) => new(false, null, message);
}
