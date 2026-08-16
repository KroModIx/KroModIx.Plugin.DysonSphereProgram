using System;

namespace KroModIx.Plugin.DysonSphereProgram.Services;

/// <summary>Ein BepInEx-Plugin unter <c>BepInEx/plugins/</c>. Kann eine
/// einzelne DLL sein (`SomePlugin.dll`) ODER ein Unterordner mit einer
/// DLL drin (`SomePlugin/SomePlugin.dll`). Beides wird von BepInEx auto-
/// discovered. Toggle via <c>.disabled</c>-Suffix (BepInEx ignoriert
/// Dateien mit Extension != .dll).</summary>
public sealed record DspMod(
    string Path,
    string Name,
    bool IsEnabled,
    bool IsDirectory,
    long SizeBytes,
    DateTime InstalledUtc);
