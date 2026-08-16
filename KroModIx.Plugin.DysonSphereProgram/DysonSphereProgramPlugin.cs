using System;
using System.Collections.Generic;
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
public sealed class DysonSphereProgramPlugin : IGameModPlugin
{
    public PluginMetadata Metadata { get; } = new(
        Id: "kroste.dysonsphereprogram",
        DisplayName: "Dyson Sphere Program Mod-Manager",
        Version: "0.3.0",
        Author: "Kroste",
        Description: "Mod-Verwaltung für Dyson Sphere Program. " +
            "v0.2.0: Drei Tabs (Installiert / Nexus-Katalog / Downloads), " +
            "BepInEx-Auto-Install-Assistent (Direct-Download vom offiziellen " +
            "BepInEx-GitHub-Release), Nexus-Voll-Katalog via GraphQL (Sort + " +
            "Search + Kategorie-Filter), SharpCompress-Auto-Layout-Install " +
            "(BepInEx/plugins/-, Flat- oder Ordner-Layout). Nexus-Downloads " +
            "sind Premium (analog Cyberpunk). DE+EN. Async Refresh (kein UI-Freeze).");

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
    private CoverCache? _covers;
    private DownloadEventBus? _bus;
    private BepInExBootstrapper? _bootstrapper;

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
        _zipInstaller = new DspZipInstaller();
        _covers = new CoverCache(host.CreateHttpClient("dsp-covers"), host);
        _bus = new DownloadEventBus();
        _bootstrapper = new BepInExBootstrapper(host.CreateHttpClient("dsp-bepinex-bootstrap"));

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
            || _zipInstaller is null || _covers is null || _bus is null || _bootstrapper is null)
            yield break;
        yield return new InstalledTab(game, _scanner, _installer, _paths, _bootstrapper, _bus, _host);
        yield return new NexusTab(_catalog, _covers, _host.Nexus, _downloader, _bus, _host);
        yield return new DownloadsTab(game, _pluginPaths, _zipInstaller, _bus, _host);
    }

    public Task ShutdownAsync()
    {
        _host?.Logger.Info("DSP shutdown");
        return Task.CompletedTask;
    }

    private sealed class InstalledTab : IGameTabContribution
    {
        private readonly DetectedGame _game;
        private readonly BepInExScanner _scanner;
        private readonly DspInstallService _installer;
        private readonly DspPathResolver _paths;
        private readonly BepInExBootstrapper _bootstrapper;
        private readonly DownloadEventBus _bus;
        private readonly IHostServices _host;

        public InstalledTab(DetectedGame game, BepInExScanner scanner,
            DspInstallService installer, DspPathResolver paths,
            BepInExBootstrapper bootstrapper, DownloadEventBus bus, IHostServices host)
        { _game = game; _scanner = scanner; _installer = installer; _paths = paths;
          _bootstrapper = bootstrapper; _bus = bus; _host = host; }

        public string Id => "installed";
        public string Label => Strings.T("tab.installed");
        public string Icon => "\U0001F9E9";
        public int Order => 0;
        public bool IsVisible(DetectedGame game) => true;
        public Control CreateView(DetectedGame game, IHostServices host) =>
            new InstalledModsView
            {
                DataContext = new InstalledModsViewModel(_game, _scanner, _installer, _paths,
                    _bootstrapper, _bus, _host),
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
        private readonly IHostServices _host;

        public DownloadsTab(DetectedGame game, DspPaths paths, DspZipInstaller installer,
            DownloadEventBus bus, IHostServices host)
        { _game = game; _paths = paths; _installer = installer; _bus = bus; _host = host; }

        public string Id => "downloads";
        public string Label => Strings.T("tab.downloads");
        public string Icon => "\U0001F4E5"; // 📥
        public int Order => 20;
        public bool IsVisible(DetectedGame game) => true;
        public Control CreateView(DetectedGame game, IHostServices host) =>
            new DownloadsView
            {
                DataContext = new DownloadsViewModel(_game, _paths, _installer, _bus, _host),
            };
    }
}
