using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using KroModIx.Plugin.DysonSphereProgram.Services;

namespace KroModIx.Plugin.DysonSphereProgram.Views;

/// <summary>Installiert-Tab: Toolbar (Refresh, Open-Folder, Bulk-Aktionen)
/// + Filter + Row-Liste. Kroste-Card-Row-Layout ohne Cover-Frame (BepInEx-
/// Plugins haben keinen Nexus-Cover-Cache in v0.1).</summary>
public sealed class InstalledModsView : UserControl
{
    public InstalledModsView()
    {
        var refreshBtn = new Button { Content = Strings.T("btn.refresh") };
        refreshBtn.Bind(Button.CommandProperty,
            new Binding(nameof(InstalledModsViewModel.RefreshCommand)));

        var openBtn = new Button { Content = Strings.T("btn.open_folder") };
        openBtn.Classes.Add("ghost");
        openBtn.Bind(Button.CommandProperty,
            new Binding(nameof(InstalledModsViewModel.OpenPluginsFolderCommand)));

        var enableAllBtn = new Button { Content = Strings.T("btn.enable_all") };
        enableAllBtn.Classes.Add("ghost");
        enableAllBtn.Bind(Button.CommandProperty,
            new Binding(nameof(InstalledModsViewModel.EnableAllCommand)));

        var disableAllBtn = new Button { Content = Strings.T("btn.disable_all") };
        disableAllBtn.Classes.Add("ghost");
        disableAllBtn.Bind(Button.CommandProperty,
            new Binding(nameof(InstalledModsViewModel.DisableAllCommand)));

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { refreshBtn, openBtn, enableAllBtn, disableAllBtn },
        };

        var status = new TextBlock { Margin = new Thickness(0, 0, 0, 4) };
        status.Classes.Add("muted");
        status.Bind(TextBlock.TextProperty,
            new Binding(nameof(InstalledModsViewModel.StatusText)));

        var filter = new TextBox
        {
            PlaceholderText = Strings.T("placeholder.search"),
            Margin = new Thickness(0, 0, 0, 8),
        };
        filter.Bind(TextBox.TextProperty,
            new Binding(nameof(InstalledModsViewModel.FilterText)) { Mode = BindingMode.TwoWay });

        var list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            SelectionMode = SelectionMode.Single,
        };
        list.Bind(ListBox.ItemsSourceProperty,
            new Binding(nameof(InstalledModsViewModel.Rows)));
        list.ItemTemplate = new FuncDataTemplate<ModRow>((row, _) =>
            row is null ? null : BuildRowCard(), true);

        Content = new DockPanel
        {
            Margin = new Thickness(16, 12),
            Children =
            {
                WithDock(toolbar, Dock.Top),
                WithDock(status, Dock.Top),
                WithDock(filter, Dock.Top),
                list,
            },
        };
    }

    private static Control BuildRowCard()
    {
        var iconFrame = new Border
        {
            Width = 60, Height = 60,
            CornerRadius = new CornerRadius(6),
            [!Border.BackgroundProperty] = new DynamicResourceExtension("KrosteSurfaceBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var icon = new TextBlock
        {
            FontSize = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon.Classes.Add("muted");
        icon.Bind(TextBlock.TextProperty, new Binding(nameof(ModRow.TypeIcon)));
        iconFrame.Child = icon;

        var name = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.Bind(TextBlock.TextProperty, new Binding("Mod.Name"));

        var subtitle = new TextBlock { FontSize = 11 };
        subtitle.Classes.Add("muted");
        subtitle.Bind(TextBlock.TextProperty, new Binding(nameof(ModRow.SubtitleText)));

        var status = new TextBlock
        {
            FontSize = 10, FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 2, 0, 0),
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(ModRow.StatusLabel)));

        var titleColumn = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            Children = { name, subtitle, status },
        };

        var toggleBtn = new Button();
        toggleBtn.Bind(Button.ContentProperty, new Binding(nameof(ModRow.ToggleButtonLabel)));
        BindRowCommand(toggleBtn, nameof(InstalledModsViewModel.ToggleEnabledCommand));

        var uninstallBtn = new Button { Content = Strings.T("btn.uninstall") };
        uninstallBtn.Classes.Add("danger");
        BindRowCommand(uninstallBtn, nameof(InstalledModsViewModel.UninstallCommand));

        var actions = new StackPanel
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { toggleBtn, uninstallBtn },
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(12, 8),
        };
        Grid.SetColumn(iconFrame, 0);
        Grid.SetColumn(titleColumn, 1);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(iconFrame);
        grid.Children.Add(titleColumn);
        grid.Children.Add(actions);

        var card = new Border { Margin = new Thickness(0, 0, 0, 6), Child = grid };
        card.Classes.Add("card");
        return card;
    }

    private static void BindRowCommand(Button btn, string commandName)
    {
        btn.Bind(Button.CommandProperty, new Binding
        {
            RelativeSource = new RelativeSource
            { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(ListBox) },
            Path = "DataContext." + commandName,
        });
        btn.Bind(Button.CommandParameterProperty, new Binding("."));
    }

    private static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
}
