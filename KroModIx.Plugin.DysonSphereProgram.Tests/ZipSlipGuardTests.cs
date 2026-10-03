using System;
using System.IO;
using System.IO.Compression;
using FluentAssertions;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.DysonSphereProgram.Services;
using KroModIx.Plugin.TestKit;
using Xunit;

namespace KroModIx.Plugin.DysonSphereProgram.Tests;

/// <summary>Ausbruch-Schutz beim Install. Die reine Pfad-Rechnung liegt seit
/// v0.9.0 nicht mehr hier, sondern in <see cref="ArchivePathSafety"/>
/// (Contracts) und wird im Host geprüft; hier steht, was <b>das Plugin</b>
/// daraus macht.
///
/// <para><b>Was sich inhaltlich geändert hat:</b> der eigene Schutz war
/// richtig gerechnet — er löste jeden Pfad gegen das Ziel auf —, aber er
/// <b>übersprang</b> den abgelehnten Eintrag still und meldete am Ende
/// Erfolg. Wer ein Archiv mit einem Ausbruchsversuch installierte, sah
/// „Direkt-Layout: 42 Datei(en)" und erfuhr nichts von der
/// dreiundvierzigsten. Jetzt bricht der Install mit Meldung ab.</para>
///
/// <para>Dazu kommt der Laufwerksbuchstabe: <c>C:\evil.dll</c> ist auf Linux
/// nicht „rooted", der Eintrag landete dort als Ordner namens <c>C:</c>
/// <b>innerhalb</b> des Ziels. Kein Ausbruch, aber auch nicht das, was der
/// Test auf Windows prüfte — der Host-Schutz prüft den Buchstaben
/// zusätzlich und verhält sich auf beiden Plattformen gleich.</para></summary>
public sealed class ZipSlipGuardTests : IDisposable
{
    private readonly string _tmp;
    private readonly string _installRoot;
    private readonly DetectedGame _game;
    private readonly FakeArchiveService _archives = new();
    private readonly DspZipInstaller _installer;

    public ZipSlipGuardTests()
    {
        _installer = new DspZipInstaller(_archives);
        _tmp = Directory.CreateTempSubdirectory("dsp-zipslip").FullName;
        _installRoot = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(_installRoot);

        _game = new DetectedGame(
            Target: new GameTarget("dyson-sphere-program", "Dyson Sphere Program", 1366540,
                Array.Empty<string>(), Platforms.Both),
            InstallDir: _installRoot,
            UserDataDir: null,
            ProtonPrefix: null,
            Runtime: RuntimeKind.Native,
            Source: GameSource.Steam);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { /* Aufräumen darf scheitern */ }
    }

    private string BuildZip(string name, params string[] entries)
    {
        var zipPath = Path.Combine(_tmp, name);
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var path in entries)
        {
            using var s = archive.CreateEntry(path).Open();
            s.Write([1, 2, 3]);
        }
        return zipPath;
    }

    /// <summary>Direkt-Layout: der Ausbruchsversuch bricht ab und wird
    /// benannt — nicht mehr still übersprungen.</summary>
    [Fact]
    public void Direkt_Layout_bricht_bei_Ausbruch_ab_statt_zu_ueberspringen()
    {
        var zip = BuildZip("boese.zip",
            "BepInEx/plugins/MeinMod.dll",
            "BepInEx/plugins/../../../../evil.dll");

        var r = _installer.Install(zip, _game);

        File.Exists(Path.Combine(_tmp, "evil.dll")).Should().BeFalse();
        r.Success.Should().BeFalse("vorher meldete derselbe Fall Erfolg");
        r.Message.Should().Contain("herausschreiben");
    }

    [Fact]
    public void Absoluter_Eintragsname_bricht_nicht_aus()
    {
        var opfer = Path.Combine(_tmp, "ausserhalb.dll");
        var zip = BuildZip("boese2.zip", "BepInEx/plugins/MeinMod.dll", opfer);

        var r = _installer.Install(zip, _game);

        File.Exists(opfer).Should().BeFalse();
        r.Success.Should().BeFalse();
    }

    /// <summary>Der Ordnername im Ordner-Layout kommt aus dem Archiv und ist
    /// damit genauso unvertrauenswürdig wie ein Eintragspfad. Geprüft wird
    /// er <b>vor</b> dem Anlegen des Ordners.</summary>
    [Fact]
    public void Unsicherer_Root_Ordnername_wird_abgelehnt()
    {
        var zip = BuildZip("boese3.zip", "../../../../evil/MeinMod.dll");

        var r = _installer.Install(zip, _game);

        r.Success.Should().BeFalse();
        r.Message.Should().Contain("Unsicherer Ordnername");
        Directory.Exists(Path.Combine(_tmp, "evil")).Should().BeFalse();
    }

    [Fact]
    public void Direkt_Layout_ohne_Ausbruch_laeuft_durch()
    {
        var zip = BuildZip("gut.zip",
            "BepInEx/plugins/MeinMod.dll",
            "BepInEx/core/BepInEx.Core.dll");

        var r = _installer.Install(zip, _game);

        r.Success.Should().BeTrue(r.Message);
        File.Exists(Path.Combine(_installRoot, "BepInEx", "plugins", "MeinMod.dll"))
            .Should().BeTrue();
        File.Exists(Path.Combine(_installRoot, "BepInEx", "core", "BepInEx.Core.dll"))
            .Should().BeTrue();
    }

    [Fact]
    public void Flaches_Layout_legt_Wurzel_Dlls_nach_plugins()
    {
        var zip = BuildZip("flach.zip", "MeinMod.dll", "liesmich.txt");

        var r = _installer.Install(zip, _game);

        r.Success.Should().BeTrue(r.Message);
        File.Exists(Path.Combine(_installRoot, "BepInEx", "plugins", "MeinMod.dll"))
            .Should().BeTrue();
        File.Exists(Path.Combine(_installRoot, "BepInEx", "plugins", "liesmich.txt"))
            .Should().BeFalse("nur DLLs auf Wurzelebene");
    }

    /// <summary>Ordner-Layout: der Ordner landet unter
    /// <c>BepInEx/plugins/</c>, und ein README <b>neben</b> dem Ordner wird
    /// übersprungen. Früher lief genau das in eine
    /// <c>ArgumentOutOfRangeException</c> im Substring; seit v0.9.0 macht das
    /// <c>StripPrefix</c> des Baukastens von sich aus.</summary>
    [Fact]
    public void Ordner_Layout_nimmt_den_Ordner_und_ueberspringt_Nachbarn()
    {
        var zip = BuildZip("ordner.zip",
            "MeinMod/MeinMod.dll",
            "MeinMod/config/standard.cfg");

        var r = _installer.Install(zip, _game);

        r.Success.Should().BeTrue(r.Message);
        File.Exists(Path.Combine(_installRoot, "BepInEx", "plugins", "MeinMod", "MeinMod.dll"))
            .Should().BeTrue();
        File.Exists(Path.Combine(_installRoot, "BepInEx", "plugins", "MeinMod",
            "config", "standard.cfg")).Should().BeTrue();
    }

    [Fact]
    public void Kein_Archiv_wird_am_Inhalt_erkannt()
    {
        var kaputt = Path.Combine(_tmp, "abgebrochen.zip");
        File.WriteAllText(kaputt, "das ist kein ZIP");

        var r = _installer.Install(kaputt, _game);

        r.Success.Should().BeFalse();
        r.Message.Should().Contain("kein lesbares Archiv");
    }
}
