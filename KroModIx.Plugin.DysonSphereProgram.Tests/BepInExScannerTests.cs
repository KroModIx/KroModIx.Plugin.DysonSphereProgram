using System.IO;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.DysonSphereProgram.Services;
using Xunit;

namespace KroModIx.Plugin.DysonSphereProgram.Tests;

public class BepInExScannerTests
{
    /// <summary>Flat DLL im plugins-Root + eine .disabled-Variante + ein Ordner-
    /// Plugin — Scanner findet alle drei und klassifiziert sauber.</summary>
    [Fact]
    public void ScanAll_DetectsFlatAndFolderPlugins()
    {
        using var temp = new TempDir();
        var pluginsDir = Path.Combine(temp.Path, "BepInEx", "plugins");
        Directory.CreateDirectory(pluginsDir);
        Directory.CreateDirectory(Path.Combine(temp.Path, "BepInEx", "core"));
        File.WriteAllText(Path.Combine(temp.Path, "BepInEx", "core", "BepInEx.dll"), "");

        File.WriteAllText(Path.Combine(pluginsDir, "ActiveMod.dll"), "test");
        File.WriteAllText(Path.Combine(pluginsDir, "OldMod.dll.disabled"), "test");
        Directory.CreateDirectory(Path.Combine(pluginsDir, "FolderMod"));
        File.WriteAllText(Path.Combine(pluginsDir, "FolderMod", "FolderMod.dll"), "test");

        var resolver = new DspPathResolver();
        var scanner = new BepInExScanner(resolver);
        var game = FakeGame(temp.Path);

        var mods = scanner.ScanAll(game);

        mods.Should().HaveCount(3);
        mods.Should().Contain(m => m.Name == "ActiveMod" && m.IsEnabled && !m.IsDirectory);
        mods.Should().Contain(m => m.Name == "OldMod" && !m.IsEnabled && !m.IsDirectory);
        mods.Should().Contain(m => m.Name == "FolderMod" && m.IsEnabled && m.IsDirectory);
    }

    [Fact]
    public void LooksLikeBepInExInstall_TrueOnlyIfCoreDllPresent()
    {
        using var temp = new TempDir();
        var resolver = new DspPathResolver();
        var game = FakeGame(temp.Path);

        resolver.LooksLikeBepInExInstall(game).Should().BeFalse();

        Directory.CreateDirectory(Path.Combine(temp.Path, "BepInEx", "core"));
        File.WriteAllText(Path.Combine(temp.Path, "BepInEx", "core", "BepInEx.dll"), "");
        resolver.LooksLikeBepInExInstall(game).Should().BeTrue();
    }

    private static DetectedGame FakeGame(string installDir) => new(
        Target: new GameTarget("dsp", "DSP", SteamAppId: 1366540,
            AlternativeExecutableNames: System.Array.Empty<string>(),
            Platforms: Platforms.Both),
        InstallDir: installDir,
        UserDataDir: null,
        ProtonPrefix: null,
        Runtime: RuntimeKind.Native,
        Source: GameSource.Steam);

    private sealed class TempDir : System.IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "dsp-scan-" + System.Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
            catch { }
        }
    }
}
