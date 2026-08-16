using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.DysonSphereProgram.Services;

namespace KroModIx.Plugin.DysonSphereProgram.Views;

/// <summary>Nexus-Katalog-Tab. Voll-Katalog via GraphQL (kein API-Key
/// noetig fuer Read). Analog Cyberpunk — Pagination, Sort, Search,
/// Kategorie-Filter clientseitig.</summary>
public sealed partial class NexusViewModel : ObservableObject, IDisposable
{
    private readonly DspNexusCatalog _catalog;
    private readonly CoverCache _covers;
    private readonly INexusService _nexus;
    private readonly DspDownloader _downloader;
    private readonly DownloadEventBus _bus;
    private readonly IHostServices _host;
    private readonly EventHandler _apiKeyChangedHandler;
    private readonly System.Threading.SemaphoreSlim _loadGate = new(1, 1);

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private NexusSortOption? _selectedSort;
    [ObservableProperty] private bool _isPremium;
    [ObservableProperty] private string? _selectedCategory = "";
    [ObservableProperty] private string _coverProgressText = "";

    partial void OnSelectedSortChanged(NexusSortOption? value) { if (value is not null) _ = LoadFirstPageAsync(); }
    partial void OnSelectedCategoryChanged(string? value) => ApplyCategoryFilter();
    partial void OnIsPremiumChanged(bool value) { foreach (var r in Rows) r.IsPremium = value; }

    public ObservableCollection<NexusRow> Rows { get; } = new();
    public ObservableCollection<string> Categories { get; } = new() { "" };
    public IReadOnlyList<NexusSortOption> SortOptions { get; } = new[]
    {
        new NexusSortOption(Strings.T("sort.latest_update"), NexusSort.LatestUpdate),
        new NexusSortOption(Strings.T("sort.latest_add"), NexusSort.LatestAdd),
        new NexusSortOption(Strings.T("sort.most_endorsed"), NexusSort.MostEndorsed),
        new NexusSortOption(Strings.T("sort.most_downloaded"), NexusSort.MostDownloaded),
    };
    public bool HasMore => _catalog.HasMore;

    public NexusViewModel(DspNexusCatalog catalog, CoverCache covers,
        INexusService nexus, DspDownloader downloader, DownloadEventBus bus, IHostServices host)
    {
        _catalog = catalog; _covers = covers; _nexus = nexus;
        _downloader = downloader; _bus = bus; _host = host;
        _selectedSort = SortOptions[0];
        IsPremium = _nexus.IsPremium;
        _apiKeyChangedHandler = (_, _) => Dispatcher.UIThread.Post(() =>
        {
            IsPremium = _nexus.IsPremium;
            _ = LoadFirstPageAsync();
        });
        _nexus.ApiKeyChanged += _apiKeyChangedHandler;
        _ = InitialLoadAsync();
    }

    public void Dispose() => _nexus.ApiKeyChanged -= _apiKeyChangedHandler;

    private async Task InitialLoadAsync()
    {
        if (_catalog.Cached.Count == 0) await LoadFirstPageAsync();
        else RebuildRowsFromCatalog();
    }

    private void RebuildRowsFromCatalog()
    {
        RefreshCategoryOptions();
        Rows.Clear();
        var filter = SelectedCategory ?? "";
        foreach (var e in _catalog.Cached)
        {
            if (!string.IsNullOrEmpty(filter)
                && !string.Equals(e.Category, filter, StringComparison.OrdinalIgnoreCase))
                continue;
            Rows.Add(new NexusRow(e) { IsPremium = IsPremium });
        }
        UpdateStatus();
        OnPropertyChanged(nameof(HasMore));
    }

    private void RefreshCategoryOptions()
    {
        var unique = _catalog.Cached
            .Select(e => e.Category?.Trim() ?? "")
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var desired = new List<string> { "" };
        desired.AddRange(unique);
        if (Categories.Count == desired.Count
            && Categories.SequenceEqual(desired, StringComparer.OrdinalIgnoreCase)) return;
        var preserve = SelectedCategory ?? "";
        Categories.Clear();
        foreach (var c in desired) Categories.Add(c);
        SelectedCategory = Categories.Contains(preserve, StringComparer.OrdinalIgnoreCase) ? preserve : "";
    }

    private void ApplyCategoryFilter()
    {
        Rows.Clear();
        var filter = SelectedCategory ?? "";
        foreach (var e in _catalog.Cached)
        {
            if (!string.IsNullOrEmpty(filter)
                && !string.Equals(e.Category, filter, StringComparison.OrdinalIgnoreCase))
                continue;
            Rows.Add(new NexusRow(e) { IsPremium = IsPremium });
        }
        UpdateStatus();
        _ = LoadCoversAsync(0);
    }

    private void UpdateStatus()
    {
        var loaded = _catalog.Cached.Count;
        var total = _catalog.TotalCount;
        StatusText = total > 0
            ? string.Format(Strings.T("status.mods_of"), loaded, total)
            : string.Format(Strings.T("status.mods_count_catalog"), loaded);
    }

    [RelayCommand]
    private async Task LoadFirstPageAsync()
    {
        if (!await _loadGate.WaitAsync(0)) return;
        try
        {
            IsBusy = true;
            StatusText = Strings.T("status.loading_catalog");
            await _catalog.LoadFirstPageAsync((SelectedSort ?? SortOptions[0]).Value, SearchQuery);
            RebuildRowsFromCatalog();
            _ = LoadCoversAsync(0);
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Nexus-Load-First fehlgeschlagen");
            StatusText = Strings.T("status.error_prefix") + ex.Message;
        }
        finally { IsBusy = false; _loadGate.Release(); }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!_catalog.HasMore) return;
        if (!await _loadGate.WaitAsync(0)) return;
        try
        {
            IsBusy = true;
            var before = _catalog.Cached.Count;
            await _catalog.LoadNextPageAsync();
            for (int i = before; i < _catalog.Cached.Count; i++)
                Rows.Add(new NexusRow(_catalog.Cached[i]) { IsPremium = IsPremium });
            UpdateStatus();
            OnPropertyChanged(nameof(HasMore));
            _ = LoadCoversAsync(before);
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Nexus-Load-More fehlgeschlagen");
            StatusText = Strings.T("status.error_prefix") + ex.Message;
        }
        finally { IsBusy = false; _loadGate.Release(); }
    }

    [RelayCommand]
    private Task SearchAsync() => LoadFirstPageAsync();

    private async Task LoadCoversAsync(int startIndex)
    {
        var snapshot = new List<NexusRow>();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            for (int i = startIndex; i < Rows.Count; i++) snapshot.Add(Rows[i]);
        });
        int pending = snapshot.Count(r => r.Cover is null && !string.IsNullOrEmpty(r.Source.PictureUrl));
        if (pending == 0) { await Dispatcher.UIThread.InvokeAsync(() => CoverProgressText = ""); return; }
        int done = 0;
        void UpdateProgress() => CoverProgressText = $"🖼 {done}/{pending}";
        await Dispatcher.UIThread.InvokeAsync(UpdateProgress);
        foreach (var row in snapshot)
        {
            if (string.IsNullOrEmpty(row.Source.PictureUrl)) continue;
            if (row.Cover is not null) continue;
            var path = await _covers.GetOrDownloadCoverAsync(row.Source.PictureUrl);
            if (path is null) { done++; await Dispatcher.UIThread.InvokeAsync(UpdateProgress); continue; }
            try
            {
                var bmp = await Task.Run(() =>
                {
                    using var s = File.OpenRead(path);
                    return new Bitmap(s);
                });
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    row.Cover = bmp; done++; UpdateProgress();
                });
            }
            catch (Exception ex)
            {
                _host.Logger.Debug(ex, "Cover-Bitmap-Load fehlgeschlagen mod_id={Id}", row.Source.ModId);
                done++; await Dispatcher.UIThread.InvokeAsync(UpdateProgress);
            }
            await Task.Delay(150);
        }
        await Dispatcher.UIThread.InvokeAsync(() => CoverProgressText = "");
    }

    [RelayCommand]
    private void OpenInBrowser(NexusRow? row)
    {
        if (row is null) return;
        _host.Shell.OpenExternalUrl(
            $"https://www.nexusmods.com/{DspNexusCatalog.GameSlug}/mods/{row.Source.ModId}");
    }

    [RelayCommand]
    private async Task DownloadAsync(NexusRow? row)
    {
        if (row is null) return;
        if (!IsPremium)
        {
            _host.Notifications.Notify(Strings.T("notify.premium_required"), NotificationLevel.Warning);
            return;
        }
        using var scope = _host.BeginProgress($"Nexus: {row.Source.Name}");
        scope.Report(0, Strings.T("btn.download"));
        try
        {
            var progress = new Progress<double>(f =>
                scope.Report(f, $"{row.Source.Name} · {(int)(f * 100)}%"));
            var target = await _downloader.DownloadPrimaryAsync(row.Source.ModId, progress);
            if (target is null)
            {
                _host.Notifications.Notify(Strings.T("notify.download_fail"), NotificationLevel.Error);
                return;
            }
            _host.Notifications.Notify(
                Strings.T("notify.download_ok_prefix") + Path.GetFileName(target),
                NotificationLevel.Success);
            _bus.RaiseDownloadsChanged(Path.GetFileName(target));
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Nexus-Download fehlgeschlagen mod_id={Id}", row.Source.ModId);
            _host.Notifications.Notify("Download-Fehler: " + ex.Message, NotificationLevel.Error);
        }
    }
}

public sealed partial class NexusRow : ObservableObject
{
    public NexusRow(NexusCatalogEntry source) => Source = source;
    public NexusCatalogEntry Source { get; }
    [ObservableProperty] private bool _isPremium;
    [ObservableProperty] private Bitmap? _cover;

    public string Name => Source.Name;
    public string Author => Source.Author;
    public string Summary => Source.Summary;
    public string VersionDisplay
    {
        get
        {
            var v = Source.Version?.Trim() ?? "";
            if (v.Length == 0) return "";
            return char.IsDigit(v[0]) ? "v" + v : v;
        }
    }
    public string EndorsementsText => Source.Endorsements > 0 ? $"👍 {Source.Endorsements}" : "";
    public string UpdatedText
    {
        get
        {
            var delta = DateTime.UtcNow - Source.UpdatedUtc;
            if (delta.TotalDays < 1) return "heute";
            if (delta.TotalDays < 2) return "gestern";
            if (delta.TotalDays < 30) return $"vor {(int)delta.TotalDays} Tagen";
            if (delta.TotalDays < 365) return $"vor {(int)(delta.TotalDays / 30)} Monaten";
            return Source.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd");
        }
    }
}

public sealed record NexusSortOption(string Label, NexusSort Value);
