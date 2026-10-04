using System;

namespace KroModIx.Plugin.DysonSphereProgram.Services;

/// <summary>Ein BepInEx-Plugin unter <c>BepInEx/plugins/</c>. Kann eine
/// einzelne DLL sein (`SomePlugin.dll`) ODER ein Unterordner mit einer
/// DLL drin (`SomePlugin/SomePlugin.dll`). Beides wird von BepInEx auto-
/// discovered. Toggle via <c>.disabled</c>-Suffix (BepInEx ignoriert
/// Dateien mit Extension != .dll).</summary>
/// <param name="ManagedBy">v0.10.0: gesetzt, wenn dieses Plugin einem
/// <b>anderen Mod-Manager</b> gehört (r2modman, Gale, Thunderstore, lmm).
/// Erkannt am Verweis, nicht am Namen — siehe
/// <see cref="KroModIx.Plugin.Contracts.ForeignManagerDetection"/>. Solche
/// Einträge werden gelistet, damit der Nutzer sieht was im Spiel liegt, aber
/// nicht verändert: BepInEx-Spiele sind der Normalfall für r2modman, und ein
/// <c>Directory.Delete(recursive)</c> auf dessen Auslieferung nimmt dem
/// Nutzer eine Mod weg, deren Quelle woanders unversehrt liegt.</param>
public sealed record DspMod(
    string Path,
    string Name,
    bool IsEnabled,
    bool IsDirectory,
    long SizeBytes,
    DateTime InstalledUtc,
    string? ManagedBy = null)
{
    /// <summary>Ob das Plugin diesen Eintrag verändern darf.</summary>
    public bool CanModify => ManagedBy is null;
}
