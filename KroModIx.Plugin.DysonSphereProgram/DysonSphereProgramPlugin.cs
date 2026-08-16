using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.DysonSphereProgram.Services;
using KroModIx.Plugin.DysonSphereProgram.Views;

namespace KroModIx.Plugin.DysonSphereProgram;

/// <summary>KroModIx-Plugin für Dyson Sphere Program. v0.1: Installiert-Tab
/// (BepInEx-Plugins unter <c>BepInEx/plugins/</c> — flat DLLs oder Ordner).
/// Enable/Disable via <c>.disabled</c>-Suffix, Uninstall, Bulk-Aktionen.
///
/// <para>Nexus-Katalog + Downloads-Tab kommen in v0.2+ (analog Cyberpunk-
/// Muster, nutzt Host-Contract <see cref="IHostServices.Nexus"/>).</para></summary>
public sealed class DysonSphereProgramPlugin : IGameModPlugin
{
    public PluginMetadata Metadata { get; } = new(
        Id: "kroste.dysonsphereprogram",
        DisplayName: "Dyson Sphere Program Mod-Manager",
        Version: "0.1.0",
        Author: "Kroste",
        Description: "Mod-Verwaltung für Dyson Sphere Program (Youthcat Studio). " +
            "v0.1: Installiert-Tab mit BepInEx-Discovery (flat DLLs unter " +
            "BepInEx/plugins/ oder Ordner-Layout), Enable/Disable via " +
            ".disabled-Suffix, Uninstall + Bulk-Aktionen. DE+EN-Uebersetzt. " +
            "Nexus-Katalog + Downloads kommen in v0.2+.");

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

    public Task InitializeAsync(IHostServices host,
        IReadOnlyList<DetectedGame> activatedGames, CancellationToken ct)
    {
        _host = host;
        // Muss VOR jedem CreateView aufgerufen werden — Views lesen Strings.T
        // im Constructor. Host garantiert Aufruf-Ordnung: InitializeAsync
        // vor GetTabContributions vor CreateView.
        Strings.Init(host.Localization);
        _paths = new DspPathResolver();
        _scanner = new BepInExScanner(_paths);
        _installer = new DspInstallService();

        foreach (var game in activatedGames)
        {
            if (_paths.LooksLikeBepInExInstall(game))
            {
                host.Logger.Info("DSP initialisiert (BepInEx erkannt): {Dir}", game.InstallDir);
            }
            else
            {
                host.Logger.Warn("DSP: kein BepInEx unter {Dir}/BepInEx/core/BepInEx.dll — " +
                    "User muss BepInEx Pack von Nexus installieren", game.InstallDir);
            }
        }
        return Task.CompletedTask;
    }

    public IEnumerable<IGameTabContribution> GetTabContributions(DetectedGame game)
    {
        if (_host is null || _paths is null || _scanner is null || _installer is null)
            yield break;
        yield return new InstalledTab(game, _scanner, _installer, _paths, _host);
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
        private readonly IHostServices _host;

        public InstalledTab(DetectedGame game, BepInExScanner scanner,
            DspInstallService installer, DspPathResolver paths, IHostServices host)
        { _game = game; _scanner = scanner; _installer = installer; _paths = paths; _host = host; }

        public string Id => "installed";
        public string Label => Strings.T("tab.installed");
        public string Icon => "\U0001F9E9"; // 🧩
        public int Order => 0;
        public bool IsVisible(DetectedGame game) => true;

        public Control CreateView(DetectedGame game, IHostServices host) =>
            new InstalledModsView
            {
                DataContext = new InstalledModsViewModel(_game, _scanner, _installer, _paths, _host),
            };
    }
}
