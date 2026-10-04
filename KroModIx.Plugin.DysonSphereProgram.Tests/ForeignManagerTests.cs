using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using KroModIx.Plugin.DysonSphereProgram.Services;
using Xunit;

namespace KroModIx.Plugin.DysonSphereProgram.Tests;

/// <summary>Mods, die ein anderer Mod-Manager ausgeliefert hat. Für
/// BepInEx-Spiele ist das der Normalfall, nicht der Ausnahmefall: r2modman,
/// Gale und Thunderstore legen ihre Profile als Verweise unter
/// <c>BepInEx/plugins/</c> ab.
///
/// <para>Der Anlass ist bezahlt — am 04.10.2026 hat ein
/// <c>Deinstallieren</c>-Klick im Icarus-Plugin lmms Pak entfernt und damit
/// lautlos eine Mod aus dem Spiel genommen. Hier wäre es ein
/// <c>Directory.Delete(recursive)</c> auf ein fremdes Profil.</para></summary>
public sealed class ForeignManagerTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("dsp-fremd").FullName;
    private readonly string _plugins;

    public ForeignManagerTests()
    {
        _plugins = Path.Combine(_tmp, "BepInEx", "plugins");
        Directory.CreateDirectory(_plugins);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { /* Aufräumen darf scheitern */ }
    }

    private string EigeneDll(string name)
    {
        var p = Path.Combine(_plugins, name);
        File.WriteAllText(p, "dll");
        return p;
    }

    /// <summary>r2modman liefert eine DLL als Verweis in sein Profil aus.</summary>
    private string FremdeDll(string name)
    {
        var profil = Path.Combine(_tmp, "config", "r2modman", "profiles", "Default");
        Directory.CreateDirectory(profil);
        var ziel = Path.Combine(profil, name);
        File.WriteAllText(ziel, "dll");
        var link = Path.Combine(_plugins, name);
        File.CreateSymbolicLink(link, ziel);
        return link;
    }

    /// <summary>…und einen Mod-Ordner ebenso.</summary>
    private string FremderOrdner(string name)
    {
        var profil = Path.Combine(_tmp, "config", "r2modman", "profiles", "Default", name);
        Directory.CreateDirectory(profil);
        File.WriteAllText(Path.Combine(profil, name + ".dll"), "dll");
        var link = Path.Combine(_plugins, name);
        Directory.CreateSymbolicLink(link, profil);
        return link;
    }

    private DspMod Scan(string pfad)
    {
        var game = new KroModIx.Plugin.Contracts.DetectedGame(
            Target: new KroModIx.Plugin.Contracts.GameTarget("dsp", "DSP", 1366540,
                Array.Empty<string>(), KroModIx.Plugin.Contracts.Platforms.Both),
            InstallDir: _tmp, UserDataDir: null, ProtonPrefix: null,
            Runtime: KroModIx.Plugin.Contracts.RuntimeKind.Native,
            Source: KroModIx.Plugin.Contracts.GameSource.Steam);
        return new BepInExScanner(new DspPathResolver()).ScanAll(game)
            .Single(m => string.Equals(Path.GetFullPath(m.Path), Path.GetFullPath(pfad),
                StringComparison.Ordinal));
    }

    [Fact]
    public void Eine_eigene_Dll_bleibt_veraenderbar()
    {
        var p = EigeneDll("MeinMod.dll");

        var mod = Scan(p);
        mod.CanModify.Should().BeTrue();
        mod.ManagedBy.Should().BeNull();
    }

    [Fact]
    public void Eine_fremd_ausgelieferte_Dll_wird_erkannt_und_benannt()
    {
        var p = FremdeDll("FremdMod.dll");

        var mod = Scan(p);
        mod.CanModify.Should().BeFalse();
        mod.ManagedBy.Should().Be("r2modman");
    }

    [Fact]
    public void Ein_fremd_ausgelieferter_Ordner_wird_auch_erkannt()
    {
        var p = FremderOrdner("FremdOrdner");

        var mod = Scan(p);
        mod.IsDirectory.Should().BeTrue();
        mod.CanModify.Should().BeFalse();
        mod.ManagedBy.Should().Be("r2modman");
    }

    [Fact]
    public void Deinstallieren_wird_verweigert_und_die_Datei_bleibt()
    {
        var p = FremdeDll("FremdMod.dll");
        var mod = Scan(p);
        var sut = new DspInstallService();

        ((Action)(() => sut.Uninstall(mod))).Should()
            .Throw<InvalidOperationException>().WithMessage("*r2modman*");

        File.Exists(p).Should().BeTrue("genau das ist am 04.10.2026 schiefgegangen");
    }

    /// <summary>Beim Ordner-Fall wäre es ein rekursives Löschen — der
    /// schlimmere der beiden.</summary>
    [Fact]
    public void Deinstallieren_eines_fremden_Ordners_wird_verweigert()
    {
        var p = FremderOrdner("FremdOrdner");
        var mod = Scan(p);
        var sut = new DspInstallService();

        ((Action)(() => sut.Uninstall(mod))).Should().Throw<InvalidOperationException>();

        Directory.Exists(p).Should().BeTrue();
    }

    [Fact]
    public void Umschalten_wird_verweigert_und_nichts_umbenannt()
    {
        var p = FremdeDll("FremdMod.dll");
        var mod = Scan(p);
        var sut = new DspInstallService();

        ((Action)(() => sut.SetEnabled(mod, false))).Should()
            .Throw<InvalidOperationException>().WithMessage("*r2modman*");

        File.Exists(p).Should().BeTrue();
        File.Exists(p + ".disabled").Should().BeFalse();
    }

    /// <summary>Gegenprobe, und die ist die wichtigere Hälfte: eine eigene
    /// Mod muss sich weiter deinstallieren lassen. Eine Sperre, die den
    /// Normalfall mitnimmt, wäre schlimmer als das Problem.</summary>
    [Fact]
    public void Eine_eigene_Mod_laesst_sich_weiter_deinstallieren()
    {
        var p = EigeneDll("MeinMod.dll");
        var mod = Scan(p);

        new DspInstallService().Uninstall(mod);

        File.Exists(p).Should().BeFalse();
    }
}
