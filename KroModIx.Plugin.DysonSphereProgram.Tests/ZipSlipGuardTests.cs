using System.IO;
using KroModIx.Plugin.DysonSphereProgram.Services;
using Xunit;

namespace KroModIx.Plugin.DysonSphereProgram.Tests;

/// <summary>Zip-Slip-Guard des Installers. Der Ordner-Layout-Pfad hatte
/// vorher gar keine Pruefung — ein Archiv mit
/// <c>Mod/../../../../evil.dll</c> haette ausserhalb von BepInEx/plugins/
/// geschrieben.</summary>
public sealed class ZipSlipGuardTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "dsp-zipslip-root");

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("../../../../evil.dll")]
    [InlineData("sub/../../evil.dll")]
    [InlineData("")]
    [InlineData("   ")]
    public void Escape_wird_abgelehnt(string relative)
        => Assert.False(DspZipInstaller.TryResolveSafe(Root, relative, out _));

    [Fact]
    public void Absolute_Pfade_werden_abgelehnt()
    {
        var absolute = OperatingSystem.IsWindows() ? @"C:\Windows\evil.dll" : "/etc/evil.dll";
        Assert.False(DspZipInstaller.TryResolveSafe(Root, absolute, out _));
    }

    [Theory]
    [InlineData("mod.dll")]
    [InlineData("sub/mod.dll")]
    [InlineData("sub/../mod.dll")]
    public void Normale_Pfade_bleiben_erlaubt(string relative)
    {
        Assert.True(DspZipInstaller.TryResolveSafe(Root, relative, out var dst));
        Assert.StartsWith(Path.GetFullPath(Root), dst);
    }
}
