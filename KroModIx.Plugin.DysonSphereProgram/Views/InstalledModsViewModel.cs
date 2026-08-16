using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.DysonSphereProgram.Services;

namespace KroModIx.Plugin.DysonSphereProgram.Views;

/// <summary>Installiert-Tab. Zeigt BepInEx-Plugins. Wenn BepInEx nicht
/// installiert ist: Bootstrap-Assistent (Nexus/BepInEx-GitHub-Auto-Download).
/// <para>v0.2: Refresh laeuft off-thread (Kernprinzip 3), BepInEx-Marker
/// wird lazy per Task.Run gecheckt. Rows-Rebuild danach auf UI-Thread.</para></summary>
public sealed partial class InstalledModsViewModel : ObservableObject, IDisposable
{
    private readonly DetectedGame _game;
    private readonly BepInExScanner _scanner;
    private readonly DspInstallService _installer;
    private readonly DspPathResolver _paths;
    private readonly BepInExBootstrapper _bootstrapper;
    private readonly DownloadEventBus _bus;
    private readonly IHostServices _host;
    private readonly EventHandler _installedHandler;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsBepInExBootstrap))]
    private string _statusText = "";

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _filterText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsBepInExBootstrap))]
    private bool _bepInExInstalled;

    public bool NeedsBepInExBootstrap => !BepInExInstalled;

    public ObservableCollection<ModRow> Rows { get; } = new();
    private List<ModRow> _allRows = new();

    public InstalledModsViewModel(DetectedGame game, BepInExScanner scanner,
        DspInstallService installer, DspPathResolver paths,
        BepInExBootstrapper bootstrapper, DownloadEventBus bus, IHostServices host)
    {
        _game = game; _scanner = scanner; _installer = installer; _paths = paths;
        _bootstrapper = bootstrapper; _bus = bus; _host = host;
        _installedHandler = (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshAsync());
        _bus.ModInstalled += _installedHandler;
        _ = RefreshAsync();
    }

    public void Dispose() => _bus.ModInstalled -= _installedHandler;

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var q = FilterText?.Trim() ?? "";
        Rows.Clear();
        var matched = string.IsNullOrEmpty(q)
            ? _allRows
            : _allRows.Where(r => r.Mod.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var r in matched) Rows.Add(r);
    }

    /// <summary>Off-thread Refresh. BepInEx-Marker-Check + Scan in Task.Run,
    /// nur Row-Rebuild + Status-Update sind UI-Thread. Kein Freeze beim
    /// initialen Plugin-Load — auch bei 100+ Plugins bleibt die App
    /// responsive (Kernprinzip 3).</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            IsBusy = true;
            var (bepInExOk, mods) = await Task.Run(() =>
            {
                var ok = _paths.LooksLikeBepInExInstall(_game);
                var scan = ok
                    ? _scanner.ScanAll(_game)
                    : (IReadOnlyList<DspMod>)Array.Empty<DspMod>();
                return (ok, scan);
            });
            BepInExInstalled = bepInExOk;
            if (!bepInExOk)
            {
                StatusText = Strings.T("status.no_bepinex");
                _allRows = new();
                Rows.Clear();
                return;
            }
            _allRows = mods.Select(m => new ModRow(m)).ToList();
            var enabled = mods.Count(m => m.IsEnabled);
            var disabled = mods.Count - enabled;
            StatusText = mods.Count == 0
                ? Strings.T("status.no_mods")
                : string.Format(Strings.T("status.mods_count"), mods.Count, enabled, disabled);
            ApplyFilter();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenPluginsFolder()
    {
        var dir = _paths.GetPluginsDir(_game);
        _host.Shell.OpenDirectory(dir);
    }

    [RelayCommand]
    private async Task InstallBepInExAsync()
    {
        var ok = await _host.Dialogs.ConfirmAsync(
            Strings.T("dialog.bepinex_install_title"),
            string.Format(Strings.T("dialog.bepinex_install_msg"), _game.InstallDir),
            okLabel: Strings.T("dialog.bepinex_install_ok"));
        if (!ok) return;

        using var scope = _host.BeginProgress(Strings.T("progress.bepinex_install"));
        try
        {
            IsBusy = true;
            _host.Notifications.Notify(Strings.T("notify.bepinex_installing"), NotificationLevel.Info);
            var progress = new Progress<double>(f =>
                scope.Report(f, $"BepInEx · {(int)(f * 100)}%"));
            var result = await _bootstrapper.InstallAsync(_game.InstallDir, progress);
            if (result.Success)
            {
                _host.Notifications.Notify(
                    string.Format(Strings.T("notify.bepinex_ok"), result.Version),
                    NotificationLevel.Success);
                await RefreshAsync();
            }
            else
            {
                _host.Notifications.Notify(
                    string.Format(Strings.T("notify.bepinex_fail"), result.ErrorMessage),
                    NotificationLevel.Error);
            }
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(ModRow? row)
    {
        if (row is null) return;
        try
        {
            IsBusy = true;
            var newPath = _installer.SetEnabled(row.Mod, !row.Mod.IsEnabled);
            row.Mod = row.Mod with { IsEnabled = !row.Mod.IsEnabled, Path = newPath };
            row.OnModChanged();
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Toggle fehlgeschlagen: {Name}", row.Mod.Name);
            await _host.Dialogs.ShowMessageAsync("Fehler", ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task UninstallAsync(ModRow? row)
    {
        if (row is null) return;
        var ok = await _host.Dialogs.ConfirmAsync(
            Strings.T("dialog.uninstall_title"),
            string.Format(Strings.T("dialog.uninstall_msg"), row.Mod.Name, row.Mod.Path),
            okLabel: Strings.T("dialog.uninstall_ok"));
        if (!ok) return;
        try
        {
            IsBusy = true;
            _installer.Uninstall(row.Mod);
            _host.Notifications.Notify(Strings.T("notify.uninstalled_prefix") + row.Mod.Name,
                NotificationLevel.Success);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Uninstall fehlgeschlagen: {Name}", row.Mod.Name);
            await _host.Dialogs.ShowMessageAsync("Fehler", ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DisableAllAsync()
    {
        var targets = Rows.Where(r => r.Mod.IsEnabled).ToList();
        if (targets.Count == 0)
        {
            _host.Notifications.Notify(Strings.T("notify.no_enabled_mods"), NotificationLevel.Info);
            return;
        }
        var ok = await _host.Dialogs.ConfirmAsync(
            Strings.T("dialog.disable_all_title"),
            string.Format(Strings.T("dialog.disable_all_msg"), targets.Count),
            okLabel: Strings.T("dialog.disable_all_ok"));
        if (!ok) return;
        int done = 0, failed = 0;
        using var scope = _host.BeginProgress(string.Format(Strings.T("progress.disable_bulk"), targets.Count));
        foreach (var row in targets)
        {
            scope.Report((double)(done + failed) / targets.Count,
                $"{done + failed + 1}/{targets.Count}: {row.Mod.Name}");
            try { _installer.SetEnabled(row.Mod, false); done++; }
            catch (Exception ex) { _host.Logger.Warn(ex, "Bulk-Disable {Name}", row.Mod.Name); failed++; }
        }
        _host.Notifications.Notify(string.Format(Strings.T("notify.bulk_disable_result"), done, failed),
            failed == 0 ? NotificationLevel.Success : NotificationLevel.Warning);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task EnableAllAsync()
    {
        var targets = Rows.Where(r => !r.Mod.IsEnabled).ToList();
        if (targets.Count == 0)
        {
            _host.Notifications.Notify(Strings.T("notify.no_disabled_mods"), NotificationLevel.Info);
            return;
        }
        int done = 0, failed = 0;
        using var scope = _host.BeginProgress(string.Format(Strings.T("progress.enable_bulk"), targets.Count));
        foreach (var row in targets)
        {
            scope.Report((double)(done + failed) / targets.Count,
                $"{done + failed + 1}/{targets.Count}: {row.Mod.Name}");
            try { _installer.SetEnabled(row.Mod, true); done++; }
            catch (Exception ex) { _host.Logger.Warn(ex, "Bulk-Enable {Name}", row.Mod.Name); failed++; }
        }
        _host.Notifications.Notify(string.Format(Strings.T("notify.bulk_enable_result"), done, failed),
            failed == 0 ? NotificationLevel.Success : NotificationLevel.Warning);
        await RefreshAsync();
    }
}

public sealed partial class ModRow : ObservableObject
{
    public ModRow(DspMod mod) => Mod = mod;
    [ObservableProperty] private DspMod _mod;

    public string StatusLabel => Mod.IsEnabled ? Strings.T("row.status_active") : Strings.T("row.status_inactive");
    public string ToggleButtonLabel => Mod.IsEnabled ? Strings.T("btn.disable") : Strings.T("btn.enable");
    public string TypeIcon => Mod.IsDirectory ? "📁" : "🧩";
    public string SizeText => Mod.SizeBytes switch
    {
        < 1024 => $"{Mod.SizeBytes} B",
        < 1024 * 1024 => $"{Mod.SizeBytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{Mod.SizeBytes / (1024.0 * 1024):F1} MB",
        _ => $"{Mod.SizeBytes / (1024.0 * 1024 * 1024):F2} GB",
    };
    public string SubtitleText
    {
        get
        {
            var parts = new List<string>
            {
                Mod.IsDirectory ? "Ordner" : "DLL",
                SizeText,
                Mod.InstalledUtc.ToLocalTime().ToString("yyyy-MM-dd"),
            };
            return string.Join(" · ", parts);
        }
    }

    public void OnModChanged()
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(ToggleButtonLabel));
        OnPropertyChanged(nameof(SubtitleText));
    }
}
