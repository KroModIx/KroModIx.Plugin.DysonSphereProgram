using FluentAssertions;
using KroModIx.Plugin.DysonSphereProgram.Services;
using Xunit;

namespace KroModIx.Plugin.DysonSphereProgram.Tests;

public class NexusFileNameParserTests
{
    // ---- Dash-Format (der reale DSP-Nexus-CDN-Standard) ----

    [Theory]
    [InlineData("Locale-15-1-0-1703155833.7z", 15, "1.0", "Locale")]
    [InlineData("DSP Cheat Menu-12-2-1-4-1703155833.zip", 12, "2.1.4", "DSP Cheat Menu")]
    [InlineData("Nebula Multiplayer-Client-42-1-8-14-1703155833.zip", 42, "1.8.14", "Nebula Multiplayer-Client")]
    [InlineData("Content Size Realizer-11-1-0-0-1703155833.rar", 11, "1.0.0", "Content Size Realizer")]
    public void DashFormat_ExtractsAllFields(string fileName, int expectedId, string expectedVer, string expectedName)
    {
        NexusFileNameParser.TryExtractModId(fileName).Should().Be(expectedId);
        NexusFileNameParser.TryExtractVersion(fileName).Should().Be(expectedVer);
        NexusFileNameParser.TryExtractModName(fileName).Should().Be(expectedName);
    }

    // ---- Space-Format (legacy, CDN-URL-Download aus Browser) ----

    [Theory]
    [InlineData("DSP Cheat Menu 12 2.1.4 2026-05-12T14-30Z abc123def.zip", 12, "2.1.4", "DSP Cheat Menu")]
    [InlineData("Liberty Stock 32353 1.1 2026-08-12T17-11Z k8lw8mSW4.rar", 32353, "1.1", "Liberty Stock")]
    public void SpaceFormat_ExtractsAllFields(string fileName, int expectedId, string expectedVer, string expectedName)
    {
        NexusFileNameParser.TryExtractModId(fileName).Should().Be(expectedId);
        NexusFileNameParser.TryExtractVersion(fileName).Should().Be(expectedVer);
        NexusFileNameParser.TryExtractModName(fileName).Should().Be(expectedName);
    }

    // ---- Nicht-Nexus-Filenames: keine ModId ----

    [Theory]
    [InlineData("some_manual_download.zip")]
    [InlineData("Mod.dll")]
    [InlineData("")]
    public void NonNexusFileNames_ReturnNull(string fileName)
    {
        NexusFileNameParser.TryExtractModId(fileName).Should().BeNull();
        NexusFileNameParser.TryExtractVersion(fileName).Should().BeNull();
        NexusFileNameParser.TryExtractModName(fileName).Should().BeNull();
    }
}
