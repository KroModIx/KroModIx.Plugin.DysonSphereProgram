using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.DysonSphereProgram.Services;

namespace KroModIx.Plugin.DysonSphereProgram.Views;

/// <summary>Downloads-Tab: listet Archive im Plugin-Downloads-Ordner,
/// bietet Install + Delete + Bulk-Install. ZIP/RAR/7z via SharpCompress.
/// Auto-Layout-Detection entscheidet zwischen direktem Extract vs
/// BepInEx/plugins/&lt;Root&gt;/-Wrap.</summary>
public sealed partial class DownloadsViewModel : ObservableObject, IDisposable
{
    private readonly DetectedGame _game;
    private readonly DspPaths _paths;
    private readonly DspZipInstaller _installer;
    private readonly DownloadEventBus _bus;
    private readonly IHostServices _host;
    private readonly EventHandler<string?> _downloadHandler;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<DownloadRow> Rows { get; } = new();

    public DownloadsViewModel(DetectedGame game, DspPaths paths,
        DspZipInstaller installer, DownloadEventBus bus, IHostServices host)
    {
        _game = game; _paths = paths; _installer = installer; _bus = bus; _host = host;
        _downloadHandler = (_, _) => Dispatcher.UIThread.Post(Refresh);
        _bus.DownloadsChanged += _downloadHandler;
        Refresh();
    }

    public void Dispose() => _bus.DownloadsChanged -= _downloadHandler;

    [RelayCommand]
    private void Refresh()
    {
        Rows.Clear();
        if (!Directory.Exists(_paths.DownloadsDir))
        {
            StatusText = string.Format(Strings.T("status.downloads_dir_missing"), _paths.DownloadsDir);
            return;
        }
        var files = Directory.EnumerateFiles(_paths.DownloadsDir)
            .Where(f => DspZipInstaller.SupportedExtensions.Any(ext =>
                f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
            .ToList();
        foreach (var f in files)
        {
            var info = new FileInfo(f);
            Rows.Add(new DownloadRow(f, info.Name, info.Length, info.LastWriteTimeUtc));
        }
        StatusText = files.Count == 0
            ? string.Format(Strings.T("status.no_zips_hint"), _paths.DownloadsDir)
            : string.Format(Strings.T("status.zips_ready"), files.Count);
    }

    [RelayCommand]
    private void OpenDownloadsFolder() => _host.Shell.OpenDirectory(_paths.DownloadsDir);

    [RelayCommand]
    private async Task InstallRowAsync(DownloadRow? row)
    {
        if (row is null) return;
        try
        {
            IsBusy = true;
            using var scope = _host.BeginProgress($"Install: {row.FileName}");
            scope.Report(0, "Extract …");
            var result = await Task.Run(() => _installer.Install(row.FilePath, _game));
            scope.Report(1.0, "OK");
            _host.Notifications.Notify(
                (result.Success ? "✓ " : "✗ ") + result.Message,
                result.Success ? NotificationLevel.Success : NotificationLevel.Error);
            if (result.Success) _bus.RaiseModInstalled();
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Install-Row fehlgeschlagen: {File}", row.FileName);
            _host.Notifications.Notify("Fehler: " + ex.Message, NotificationLevel.Error);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task InstallAllAsync()
    {
        if (Rows.Count == 0) return;
        var ok = await _host.Dialogs.ConfirmAsync(
            Strings.T("dialog.install_all_title"),
            string.Format(Strings.T("dialog.install_all_msg"), Rows.Count),
            okLabel: Strings.T("dialog.install_all_ok"));
        if (!ok) return;
        int done = 0, failed = 0;
        using var scope = _host.BeginProgress(string.Format(Strings.T("progress.install_zips"), Rows.Count));
        var snapshot = Rows.ToList();
        foreach (var row in snapshot)
        {
            scope.Report((double)(done + failed) / snapshot.Count,
                $"{done + failed + 1}/{snapshot.Count}: {row.FileName}");
            try
            {
                var r = await Task.Run(() => _installer.Install(row.FilePath, _game));
                if (r.Success) done++; else failed++;
            }
            catch { failed++; }
        }
        _host.Notifications.Notify(
            string.Format(Strings.T("notify.bulk_install_result"), done, failed),
            failed == 0 ? NotificationLevel.Success : NotificationLevel.Warning);
        _bus.RaiseModInstalled();
    }

    [RelayCommand]
    private async Task DeleteRowAsync(DownloadRow? row)
    {
        if (row is null) return;
        var ok = await _host.Dialogs.ConfirmAsync(
            Strings.T("dialog.delete_zip_title"),
            string.Format(Strings.T("dialog.delete_zip_msg"), row.FileName),
            okLabel: Strings.T("dialog.delete_zip_ok"));
        if (!ok) return;
        try { File.Delete(row.FilePath); Refresh(); }
        catch (Exception ex) { _host.Notifications.Notify("Delete-Fehler: " + ex.Message, NotificationLevel.Error); }
    }
}

public sealed record DownloadRow(string FilePath, string FileName, long SizeBytes, DateTime DownloadedUtc)
{
    public string SizeText => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024):F1} MB",
        _ => $"{SizeBytes / (1024.0 * 1024 * 1024):F2} GB",
    };
    public string SubtitleText => $"{SizeText} · {DownloadedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
}
