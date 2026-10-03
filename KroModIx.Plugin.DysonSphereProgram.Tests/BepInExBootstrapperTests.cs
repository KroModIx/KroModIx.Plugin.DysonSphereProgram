using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KroModIx.Plugin.DysonSphereProgram.Services;
using KroModIx.Plugin.TestKit;
using Xunit;

namespace KroModIx.Plugin.DysonSphereProgram.Tests;

/// <summary>Der BepInEx-Bootstrap war bis v0.9.0 <b>ungetestet</b> — und
/// trug eine fest hinterlegte Ausweich-URL auf <c>v5.4.23.5</c>, die mit
/// jeder neuen BepInEx-Ausgabe weiter veraltet wäre.
///
/// <para>Geprüft wird, <b>welche URL</b> der Bootstrap anfragt: das ist die
/// Entscheidung, die er verantwortet. Der Download läuft über eine Attrappe,
/// die immer dasselbe ZIP ausliefert und die Adresse mitschreibt.</para></summary>
public sealed class BepInExBootstrapperTests : IDisposable
{
    private const string Repo = "BepInEx/BepInEx";

    private readonly string _tmp;
    private readonly string _installDir;
    private readonly FakeGitHubService _gh = new();
    private readonly FakeArchiveService _archives = new();
    private readonly AuslieferHandler _handler;
    private readonly BepInExBootstrapper _sut;

    public BepInExBootstrapperTests()
    {
        _tmp = Directory.CreateTempSubdirectory("kromodix-dsp-bepinex").FullName;
        _installDir = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(_installDir);
        _handler = new AuslieferHandler(BaueZip("winhttp.dll", "doorstop_config.ini",
            "BepInEx/core/BepInEx.Core.dll"));
        _sut = new BepInExBootstrapper(new HttpClient(_handler), _gh, _archives);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { /* Aufräumen darf scheitern */ }
    }

    private static byte[] BaueZip(params string[] pfade)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var pfad in pfade)
            {
                using var s = zip.CreateEntry(pfad).Open();
                s.Write([1, 2, 3]);
            }
        return ms.ToArray();
    }

    [Fact]
    public async Task Nimmt_die_Mono_Fassung_aus_der_neuesten_Ausgabe()
    {
        _gh.AddRelease(Repo, "v5.4.23.5", "BepInEx_unix_x64_5.4.23.5.zip",
            "BepInEx_win_x64_5.4.23.5.zip", "BepInEx_win_x86_5.4.23.5.zip");

        var r = await _sut.InstallAsync(_installDir, ct: TestContext.Current.CancellationToken);

        r.Success.Should().BeTrue(r.ErrorMessage);
        r.Version.Should().Be("v5.4.23.5");
        _handler.Angefragt.Should().ContainSingle().Which.Should().Be(
            $"https://github.com/{Repo}/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip");
        File.Exists(Path.Combine(_installDir, "winhttp.dll")).Should().BeTrue();
        File.Exists(Path.Combine(_installDir, "BepInEx", "core", "BepInEx.Core.dll"))
            .Should().BeTrue();
    }

    /// <summary>Die v6-Vorabausgaben heißen
    /// <c>BepInEx-Unity.IL2CPP-win-x64-*</c> — Bindestriche statt
    /// Unterstriche. DSP ist Unity <b>Mono</b>; die IL2CPP-Fassung würde
    /// nicht laden. Der Vergleich läuft deshalb über Anfang <i>und</i> Ende
    /// des Namens.</summary>
    [Fact]
    public async Task Nimmt_nicht_die_IL2CPP_Vorabausgabe()
    {
        _gh.AddRelease(Repo, "v6.0.0-pre.2",
            "BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip",
            "BepInEx-Unity.Mono-win-x64-6.0.0-pre.2.zip");
        _gh.AddRelease(Repo, "v5.4.23.5", "BepInEx_win_x64_5.4.23.5.zip");

        var r = await _sut.InstallAsync(_installDir, ct: TestContext.Current.CancellationToken);

        r.Version.Should().Be("v5.4.23.5",
            "die v6-Namen passen nicht auf das Unterstrich-Muster");
        _handler.Angefragt.Should().ContainSingle()
            .Which.Should().EndWith("BepInEx_win_x64_5.4.23.5.zip");
        r.Success.Should().BeTrue(r.ErrorMessage);
    }

    /// <summary>Der eigentliche Grund für die Migration — und bei BepInEx
    /// mit einer Zusatzschwierigkeit: der Dateiname <b>trägt die Version</b>.
    /// Greift die Raten-Sperre, kennt der Baukasten nur den Tag
    /// <c>v5.4.23.5</c>; der Dateiname braucht die Fassung ohne <c>v</c>.
    /// Genau die liefert <c>GitHubRelease.Version</c>.</summary>
    [Fact]
    public async Task Bei_Raten_Sperre_entsteht_der_Dateiname_aus_der_Fassung()
    {
        _gh.AddRelease(Repo, "v5.4.23.5", "BepInEx_win_x64_5.4.23.5.zip");
        _gh.RateLimited = true;

        var r = await _sut.InstallAsync(_installDir, ct: TestContext.Current.CancellationToken);

        r.Success.Should().BeTrue(r.ErrorMessage);
        _handler.Angefragt.Should().ContainSingle().Which.Should().Be(
            $"https://github.com/{Repo}/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip",
            "Tag mit v, Dateiname ohne — das ist die Konvention von BepInEx");
    }

    /// <summary>Ohne Ausgabe wird nicht geraten, sondern gemeldet. Vorher
    /// hätte der Bootstrap die fest hinterlegte URL geladen und so getan,
    /// als wäre alles in Ordnung.</summary>
    [Fact]
    public async Task Ohne_Ausgabe_wird_gemeldet_statt_geraten()
    {
        var r = await _sut.InstallAsync(_installDir, ct: TestContext.Current.CancellationToken);

        r.Success.Should().BeFalse();
        r.ErrorMessage.Should().Contain("Kein BepInEx-Release gefunden");
        _handler.Angefragt.Should().BeEmpty();
        Directory.GetFiles(_installDir).Should().BeEmpty();
    }

    [Fact]
    public async Task Ausbruchsversuch_im_Release_bricht_ab()
    {
        _gh.AddRelease(Repo, "v5.4.23.5", "BepInEx_win_x64_5.4.23.5.zip");
        var opfer = Path.Combine(_tmp, "ausserhalb.dll");
        _handler.Inhalt = BaueZip("winhttp.dll", opfer);

        var r = await _sut.InstallAsync(_installDir, ct: TestContext.Current.CancellationToken);

        File.Exists(opfer).Should().BeFalse();
        r.Success.Should().BeFalse();
        r.ErrorMessage.Should().Contain("sollte bei einem offiziellen Release nicht vorkommen");
    }

    private sealed class AuslieferHandler(byte[] inhalt) : HttpMessageHandler
    {
        public List<string> Angefragt { get; } = [];
        public byte[] Inhalt { get; set; } = inhalt;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Angefragt.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Inhalt),
            });
        }
    }
}
