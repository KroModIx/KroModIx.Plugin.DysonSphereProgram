using System.Collections.Generic;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.DysonSphereProgram.Services;

/// <summary>Uebersetzungs-Tabelle fuer alle User-facing Strings im Dyson-
/// Sphere-Program-Plugin. Cyberpunk-Muster: <c>Init</c> beim Plugin-Ctor,
/// dann <c>T(key)</c> ueberall. Key-as-Fallback bei fehlender Uebersetzung.</summary>
public static class Strings
{
    private static ILocalization? _loc;

    public static void Init(ILocalization loc) => _loc = loc;

    public static string T(string key)
    {
        var iso = _loc?.CurrentIso ?? "de";
        if (iso.StartsWith("en") && En.TryGetValue(key, out var en)) return en;
        if (De.TryGetValue(key, out var de)) return de;
        return key;
    }

    private static readonly Dictionary<string, string> De = new()
    {
        ["tab.installed"] = "Installiert",

        ["btn.refresh"] = "🔄  Aktualisieren",
        ["btn.open_folder"] = "📂  BepInEx/plugins/ öffnen",
        ["btn.enable"] = "▶  Aktivieren",
        ["btn.disable"] = "⏸  Deaktivieren",
        ["btn.uninstall"] = "🗑  Deinstallieren",
        ["btn.enable_all"] = "▶▶  Alle aktivieren",
        ["btn.disable_all"] = "⏸⏸  Alle deaktivieren",

        ["placeholder.search"] = "🔍 Filter nach Name …",

        ["status.no_bepinex"] = "BepInEx nicht installiert unter BepInEx/plugins/. Installiere BepInEx-Pack fuer DSP (Nexus Mods).",
        ["status.no_mods"] = "Keine Plugins in BepInEx/plugins/.",
        ["status.mods_count"] = "{0} Plugin(s) — {1} aktiv, {2} deaktiviert.",

        ["row.status_active"] = "aktiv",
        ["row.status_inactive"] = "deaktiviert",

        ["notify.uninstalled_prefix"] = "Deinstalliert: ",
        ["notify.no_enabled_mods"] = "Keine aktiven Plugins.",
        ["notify.no_disabled_mods"] = "Keine deaktivierten Plugins.",
        ["notify.bulk_disable_result"] = "{0} deaktiviert, {1} Fehler.",
        ["notify.bulk_enable_result"] = "{0} aktiviert, {1} Fehler.",

        ["dialog.uninstall_title"] = "Deinstallieren?",
        ["dialog.uninstall_msg"] = "{0} wirklich löschen?\n\nPfad: {1}",
        ["dialog.uninstall_ok"] = "Löschen",
        ["dialog.disable_all_title"] = "Alle deaktivieren?",
        ["dialog.disable_all_msg"] = "{0} Plugin(s) werden per .disabled-Suffix deaktiviert. Kein Datei-Verlust — jederzeit reversibel.",
        ["dialog.disable_all_ok"] = "Deaktivieren",

        ["progress.disable_bulk"] = "Deaktiviere {0} Plugin(s) …",
        ["progress.enable_bulk"] = "Aktiviere {0} Plugin(s) …",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["tab.installed"] = "Installed",

        ["btn.refresh"] = "🔄  Refresh",
        ["btn.open_folder"] = "📂  Open BepInEx/plugins/",
        ["btn.enable"] = "▶  Enable",
        ["btn.disable"] = "⏸  Disable",
        ["btn.uninstall"] = "🗑  Uninstall",
        ["btn.enable_all"] = "▶▶  Enable all",
        ["btn.disable_all"] = "⏸⏸  Disable all",

        ["placeholder.search"] = "🔍 Filter by name …",

        ["status.no_bepinex"] = "BepInEx not installed under BepInEx/plugins/. Install the BepInEx pack for DSP (from Nexus Mods).",
        ["status.no_mods"] = "No plugins in BepInEx/plugins/.",
        ["status.mods_count"] = "{0} plugin(s) — {1} active, {2} disabled.",

        ["row.status_active"] = "active",
        ["row.status_inactive"] = "disabled",

        ["notify.uninstalled_prefix"] = "Uninstalled: ",
        ["notify.no_enabled_mods"] = "No active plugins.",
        ["notify.no_disabled_mods"] = "No disabled plugins.",
        ["notify.bulk_disable_result"] = "{0} disabled, {1} error(s).",
        ["notify.bulk_enable_result"] = "{0} enabled, {1} error(s).",

        ["dialog.uninstall_title"] = "Uninstall?",
        ["dialog.uninstall_msg"] = "Really delete {0}?\n\nPath: {1}",
        ["dialog.uninstall_ok"] = "Delete",
        ["dialog.disable_all_title"] = "Disable all?",
        ["dialog.disable_all_msg"] = "{0} plugin(s) will be disabled via .disabled suffix. No data loss — fully reversible.",
        ["dialog.disable_all_ok"] = "Disable",

        ["progress.disable_bulk"] = "Disabling {0} plugin(s) …",
        ["progress.enable_bulk"] = "Enabling {0} plugin(s) …",
    };
}
