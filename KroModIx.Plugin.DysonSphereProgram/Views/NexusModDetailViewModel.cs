using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.DysonSphereProgram.Services;

namespace KroModIx.Plugin.DysonSphereProgram.Views;

/// <summary>Nexus-Detail-Dialog v0.5: volle Mod-Beschreibung + Cover +
/// KI-Zusammenfassung via <see cref="IAiService"/>. Kein Download-Button
/// hier (der bleibt in der Katalog-Row) — dieser Dialog ist eher
/// Inspektion vor dem Klick.</summary>
public sealed partial class NexusModDetailViewModel : ObservableObject
{
    private readonly NexusRow _row;
    private readonly INexusService _nexus;
    private readonly CoverCache _covers;
    private readonly IHostServices _host;

    [ObservableProperty] private Bitmap? _cover;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _versionDisplay = "";
    [ObservableProperty] private string _updatedText = "";
    [ObservableProperty] private string _endorsementsText = "";
    [ObservableProperty] private string _summaryShort = "";
    [ObservableProperty] private string _descriptionText = "";
    [ObservableProperty] private bool _descriptionBusy;

    [ObservableProperty] private string _aiSummary = "";
    [ObservableProperty] private bool _aiBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAiSummary))]
    private bool _aiVisible;
    public bool HasAiSummary => !string.IsNullOrWhiteSpace(AiSummary);

    public NexusModDetailViewModel(NexusRow row, INexusService nexus,
        CoverCache covers, IHostServices host)
    {
        _row = row; _nexus = nexus; _covers = covers; _host = host;

        Title = row.Source.Name;
        Author = row.Source.Author;
        VersionDisplay = row.VersionDisplay;
        UpdatedText = row.UpdatedText;
        EndorsementsText = row.EndorsementsText;
        SummaryShort = row.Source.Summary;
        Cover = row.Cover;

        DescriptionText = Strings.T("detail.desc_loading");
        _ = LoadDetailAsync();
    }

    private async Task LoadDetailAsync()
    {
        try
        {
            DescriptionBusy = true;
            var detail = await _nexus.GetModDetailAsync(DspNexusCatalog.GameSlug, _row.Source.ModId);
            if (detail is null)
            {
                DescriptionText = Strings.T("detail.desc_load_error");
                return;
            }
            var html = detail.DescriptionHtml ?? "";
            var text = string.IsNullOrWhiteSpace(html)
                ? Strings.T("detail.desc_empty")
                : HtmlToText(html);
            DescriptionText = text;

            // Falls Katalog-Row keinen Cover hatte, Detail liefert oft doch einen.
            if (Cover is null && !string.IsNullOrEmpty(detail.PictureUrl))
            {
                var bytes = await _covers.GetOrDownloadBytesAsync(detail.PictureUrl);
                if (bytes is not null)
                {
                    var bmp = await _host.Images.DecodeAsync(bytes);
                    if (bmp is not null)
                        await Dispatcher.UIThread.InvokeAsync(() => Cover = bmp);
                }
            }
        }
        catch (Exception ex)
        {
            _host.Logger.Debug(ex, "Detail-Fetch fehlgeschlagen mod_id={Id}", _row.Source.ModId);
            DescriptionText = Strings.T("detail.desc_load_error") + " " + ex.Message;
        }
        finally { DescriptionBusy = false; }
    }

    [RelayCommand]
    private async Task SummarizeAsync()
    {
        if (AiBusy) return;
        if (!await _host.Ai.IsAvailableAsync())
        {
            _host.Notifications.Notify(Strings.T("notify.ai_unavailable"),
                NotificationLevel.Warning);
            return;
        }
        try
        {
            AiBusy = true;
            AiVisible = true;
            AiSummary = string.Format(Strings.T("detail.ai_running_prefix"), _host.Ai.ProviderInfo);
            var systemPrompt = Strings.T("ai.prompt.summary_system");
            var userPrompt = $"Titel: {Title}\nAutor: {Author}\n\nBeschreibung:\n{DescriptionText}";
            var answer = await _host.Ai.CompleteAsync(systemPrompt, userPrompt);
            AiSummary = string.IsNullOrWhiteSpace(answer)
                ? Strings.T("detail.ai_no_answer")
                : answer;
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "AI-Summary fehlgeschlagen mod_id={Id}", _row.Source.ModId);
            AiSummary = Strings.T("detail.ai_error") + " " + ex.Message;
        }
        finally { AiBusy = false; }
    }

    [RelayCommand]
    private void OpenOnNexus() =>
        _host.Shell.OpenExternalUrl(
            $"https://www.nexusmods.com/{DspNexusCatalog.GameSlug}/mods/{_row.Source.ModId}");

    /// <summary>Rudimentaerer HTML-→-Text-Parser fuer Nexus-Descriptions.
    /// Nexus-BBCode-Konvertierung liefert oft `&lt;br&gt;` + `&lt;p&gt;` + `&lt;strong&gt;`. Wir
    /// strippen Tags und decodieren HTML-Entities. Kein Renderer, aber
    /// ausreichend fuer Anzeige.</summary>
    private static string HtmlToText(string html)
    {
        var s = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</p\s*>", "\n\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<[^>]+>", ""); // alle anderen Tags weg
        s = System.Net.WebUtility.HtmlDecode(s);
        // Doppelte Leerzeilen kollabieren
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        return s.Trim();
    }
}
