using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.DysonSphereProgram.Services;
using KroModIx.Plugin.DysonSphereProgram.Views;

namespace KroModIx.Plugin.DysonSphereProgram;

/// <summary>KroModIx-Plugin für Dyson Sphere Program.
/// v0.2: Drei Tabs (Installiert / Nexus / Downloads) + BepInEx-Bootstrap-
/// Assistent (direkter Download vom offiziellen GitHub-Release).
/// Nutzt Host-Contract IHostServices.Nexus fuer den Katalog (Contracts v1.15+,
/// oeffentliches GraphQL). SharpCompress fuer ZIP/RAR/7z-Install.</summary>
public sealed class DysonSphereProgramPlugin : IGameModPlugin, IUpdateNotifier
{
    public PluginMetadata Metadata { get; } = new(
        Id: "kroste.dysonsphereprogram",
        DisplayName: "Dyson Sphere Program Mod-Manager",
        Version: "0.6.4",
        Author: "Kroste",
        Description: "Mod-Verwaltung für Dyson Sphere Program. v0.6.4: " +
            "Update-Checker filtert verwaiste Install-Manifests (Mod-DLL nicht " +
            "mehr auf der Platte → Manifest wird geloescht statt als Phantom-" +
            "Update-Kandidat weiter zu koennen). Fixt den grauen ↑-Badge auf " +
            "DSP-Kacheln obwohl gar keine Mods installiert sind (Test-Install-" +
            "Reste, manuell geloeschte DLLs). v0.6.3: Detail-" +
            "Dialog rendert Rich-HTML via _host.Descriptions.CreateRichView (Host " +
            "v1.21 HtmlRenderer-Baukasten) — Bold/Italic/Farben/Bilder/Listen inline " +
            "sichtbar statt Plain-Text-Wall. Plain-Text bleibt fuer KI-Prompts. " +
            "v0.6.2: HTML/BBCode-Description-Parser aus _host.Descriptions " +
            "(zentraler Baukasten Contracts v1.19). v0.6.1: NexusFileNameParser " +
            "matcht jetzt das reale DSP-Dash-Format (Locale-15-1-0-1703155833.7z) " +
            "+ .rar + .7z — vorher nur ISO-Space-Format → Cover/Details waren in " +
            "Downloads/Installed leer. Stale-Manifest-Repair haengt sich beim " +
            "ersten Refresh nach Update an alte NexusModId=null-Manifests. v0.6.0: " +
            "Row-Konsistenz in allen drei Tabs. v0.5.0: Nexus-Detail-Dialog + KI. " +
            "v0.4.0: IUpdateNotifier + Install-Manifest. v0.3.0: Host-Image-Decoder. " +
            "BepInEx-Auto-Install-Assistent. DE+EN.");

    public IReadOnlyList<GameTarget> Targets { get; } = new[]
    {
        new GameTarget(
            GameId: "dyson-sphere-program",
            DisplayName: "Dyson Sphere Program",
            SteamAppId: 1366540,
            AlternativeExecutableNames: new[] { "DSPGAME.exe" },
            Platforms: Platforms.Both),
    };

    private IHostServices? _host;
    private DspPathResolver? _paths;
    private BepInExScanner? _scanner;
    private DspInstallService? _installer;
    private DspPaths? _pluginPaths;
    private DspNexusCatalog? _catalog;
    private DspDownloader? _downloader;
    private DspZipInstaller? _zipInstaller;
    private DspInstallManifestStore? _manifests;
    private DspUpdateChecker? _updateChecker;
    private CoverCache? _covers;
    private DownloadEventBus? _bus;
    private BepInExBootstrapper? _bootstrapper;
    private DspNexusRowEnricher? _enricher;
    private IReadOnlyList<DetectedGame> _activatedGames = Array.Empty<DetectedGame>();

    public Task InitializeAsync(IHostServices host,
        IReadOnlyList<DetectedGame> activatedGames, CancellationToken ct)
    {
        _host = host;
        Strings.Init(host.Localization);
        _paths = new DspPathResolver();
        _scanner = new BepInExScanner(_paths);
        _installer = new DspInstallService();
        _pluginPaths = new DspPaths(host);
        _catalog = new DspNexusCatalog(host.Nexus);
        _downloader = new DspDownloader(host.Nexus,
            host.CreateHttpClient("dsp-downloads"), _pluginPaths);
        _manifests = new DspInstallManifestStore(host);
        _zipInstaller = new DspZipInstaller(_manifests);
        _updateChecker = new DspUpdateChecker(_manifests, _catalog);
        // v0.6.4: dem UpdateChecker die Liste aktuell installierter Mods
        // liefern — er filtert damit verwaiste Manifests (User loeschte
        // die DLL manuell, Manifest blieb → Phantom-Update-Badge).
        _updateChecker.InstalledKeysProvider = () =>
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in _activatedGames)
            {
                try
                {
                    foreach (var mod in _scanner.ScanAll(g))
                        keys.Add(DspInstallManifestStore.BuildKey(mod.Name));
                }
                catch (Exception ex) { host.Logger.Debug(ex, "Scan fuer Manifest-GC fehlgeschlagen: {Dir}", g.InstallDir); }
            }
            return keys;
        };
        _covers = new CoverCache(host.CreateHttpClient("dsp-covers"), host);
        _bus = new DownloadEventBus();
        _bootstrapper = new BepInExBootstrapper(host.CreateHttpClient("dsp-bepinex-bootstrap"));
        _enricher = new DspNexusRowEnricher(host.Nexus, _covers, host);
        _activatedGames = activatedGames;

        // v0.4: Auto-Update-Check nach 15s Bootstrap-Delay (analog Cyberpunk).
        // Nach jedem ModInstalled-Event via DownloadEventBus erneut triggern
        // damit der Sidebar-Badge nach Install sinkt.
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(15), ct); } catch { return; }
            try { await _updateChecker.CheckAsync(ct); }
            catch (Exception ex) { host.Logger.Debug(ex, "Auto-Update-Check fehlgeschlagen"); }
            try { await host.RequestUpdateBadgeRefreshAsync(); } catch { }
        }, ct);
        _bus.ModInstalled += (_, _) =>
        {
            _ = Task.Run(async () =>
            {
                try { await _updateChecker.CheckAsync(); } catch { }
                try { await host.RequestUpdateBadgeRefreshAsync(); } catch { }
            });
        };

        foreach (var game in activatedGames)
        {
            if (_paths.LooksLikeBepInExInstall(game))
                host.Logger.Info("DSP initialisiert (BepInEx erkannt): {Dir}", game.InstallDir);
            else
                host.Logger.Info("DSP initialisiert — BepInEx fehlt (Bootstrap-Assistent im Installiert-Tab): {Dir}",
                    game.InstallDir);
        }
        return Task.CompletedTask;
    }

    public IEnumerable<IGameTabContribution> GetTabContributions(DetectedGame game)
    {
        if (_host is null || _paths is null || _scanner is null || _installer is null
            || _pluginPaths is null || _catalog is null || _downloader is null
            || _zipInstaller is null || _covers is null || _bus is null || _bootstrapper is null
            || _manifests is null || _enricher is null)
            yield break;
        yield return new InstalledTab(game, _scanner, _installer, _paths, _bootstrapper, _bus,
            _manifests, _host.Nexus, _covers, _enricher, _host);
        yield return new NexusTab(_catalog, _covers, _host.Nexus, _downloader, _bus, _host);
        yield return new DownloadsTab(game, _pluginPaths, _zipInstaller, _bus,
            _host.Nexus, _covers, _enricher, _host);
    }

    public Task ShutdownAsync()
    {
        _host?.Logger.Info("DSP shutdown");
        return Task.CompletedTask;
    }

    // ---- IUpdateNotifier (v0.4) ----

    public Task<IReadOnlyList<GameUpdateInfo>> GetPendingUpdatesAsync(CancellationToken ct)
    {
        if (_updateChecker is null || _activatedGames.Count == 0)
            return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(Array.Empty<GameUpdateInfo>());
        var count = _updateChecker.PendingCount;
        if (count <= 0)
            return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(Array.Empty<GameUpdateInfo>());
        var summary = count == 1
            ? $"1 Mod-Update verfuegbar: {_updateChecker.Pending[0].InstalledName}"
            : $"{count} Mod-Updates verfuegbar";
        var infos = _activatedGames
            .Where(g => g.Target.SteamAppId is int)
            .Select(g => new GameUpdateInfo(g.Target.SteamAppId!.Value, count, summary))
            .ToList();
        return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(infos);
    }

    private sealed class InstalledTab : IGameTabContribution
    {
        private readonly DetectedGame _game;
        private readonly BepInExScanner _scanner;
        private readonly DspInstallService _installer;
        private readonly DspPathResolver _paths;
        private readonly BepInExBootstrapper _bootstrapper;
        private readonly DownloadEventBus _bus;
        private readonly DspInstallManifestStore _manifests;
        private readonly INexusService _nexus;
        private readonly CoverCache _covers;
        private readonly DspNexusRowEnricher _enricher;
        private readonly IHostServices _host;

        public InstalledTab(DetectedGame game, BepInExScanner scanner,
            DspInstallService installer, DspPathResolver paths,
            BepInExBootstrapper bootstrapper, DownloadEventBus bus,
            DspInstallManifestStore manifests, INexusService nexus,
            CoverCache covers, DspNexusRowEnricher enricher, IHostServices host)
        { _game = game; _scanner = scanner; _installer = installer; _paths = paths;
          _bootstrapper = bootstrapper; _bus = bus; _manifests = manifests;
          _nexus = nexus; _covers = covers; _enricher = enricher; _host = host; }

        public string Id => "installed";
        public string Label => Strings.T("tab.installed");
        public string Icon => "\U0001F9E9";
        public int Order => 0;
        public bool IsVisible(DetectedGame game) => true;
        public Control CreateView(DetectedGame game, IHostServices host) =>
            new InstalledModsView
            {
                DataContext = new InstalledModsViewModel(_game, _scanner, _installer, _paths,
                    _bootstrapper, _bus, _manifests, _nexus, _covers, _enricher, _host),
            };
    }

    private sealed class NexusTab : IGameTabContribution
    {
        private readonly DspNexusCatalog _catalog;
        private readonly CoverCache _covers;
        private readonly INexusService _nexus;
        private readonly DspDownloader _downloader;
        private readonly DownloadEventBus _bus;
        private readonly IHostServices _host;

        public NexusTab(DspNexusCatalog catalog, CoverCache covers, INexusService nexus,
            DspDownloader downloader, DownloadEventBus bus, IHostServices host)
        { _catalog = catalog; _covers = covers; _nexus = nexus; _downloader = downloader;
          _bus = bus; _host = host; }

        public string Id => "nexus";
        public string Label => Strings.T("tab.nexus");
        public string Icon => "\U0001F310"; // 🌐
        public int Order => 10;
        public bool IsVisible(DetectedGame game) => true;
        public Control CreateView(DetectedGame game, IHostServices host) =>
            new NexusView
            {
                DataContext = new NexusViewModel(_catalog, _covers, _nexus, _downloader, _bus, _host),
            };
    }

    private sealed class DownloadsTab : IGameTabContribution
    {
        private readonly DetectedGame _game;
        private readonly DspPaths _paths;
        private readonly DspZipInstaller _installer;
        private readonly DownloadEventBus _bus;
        private readonly INexusService _nexus;
        private readonly CoverCache _covers;
        private readonly DspNexusRowEnricher _enricher;
        private readonly IHostServices _host;

        public DownloadsTab(DetectedGame game, DspPaths paths, DspZipInstaller installer,
            DownloadEventBus bus, INexusService nexus, CoverCache covers,
            DspNexusRowEnricher enricher, IHostServices host)
        { _game = game; _paths = paths; _installer = installer; _bus = bus;
          _nexus = nexus; _covers = covers; _enricher = enricher; _host = host; }

        public string Id => "downloads";
        public string Label => Strings.T("tab.downloads");
        public string Icon => "\U0001F4E5"; // 📥
        public int Order => 20;
        public bool IsVisible(DetectedGame game) => true;
        public Control CreateView(DetectedGame game, IHostServices host) =>
            new DownloadsView
            {
                DataContext = new DownloadsViewModel(_game, _paths, _installer, _bus,
                    _nexus, _covers, _enricher, _host),
            };
    }
}
