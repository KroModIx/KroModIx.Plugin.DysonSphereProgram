using System.Text.RegularExpressions;

namespace KroModIx.Plugin.DysonSphereProgram.Services;

/// <summary>Extrahiert Nexus-Mod-Id + Version + Name aus einem Nexus-CDN-
/// Filename. Format (empirisch, gleicher Parser wie Cyberpunk-Plugin):
/// <c>&lt;Mod Name&gt; &lt;mod_id&gt; &lt;version&gt; &lt;yyyy-MM-ddTHH-mmZ&gt; &lt;hash&gt;.zip</c>
///
/// <para>Beispiele:</para>
/// <list type="bullet">
/// <item><c>DSP Cheat Menu 12 2.1.4 2026-05-12T14-30Z abc123def.zip</c> → mod_id=12, ver=2.1.4</item>
/// </list>
///
/// <para>Regex-Anchor: Timestamp-Muster + Hash + <c>.zip</c>. Ohne Nexus-CDN-
/// Naming (User haut manuell eine ZIP rein) liefert der Parser null —
/// Consumer laesst dann Manifest leer.</para></summary>
public static class NexusFileNameParser
{
    private static readonly Regex Pattern = new(
        @"^(?<name>.*?)\s+(?<modId>\d+)\s+(?<version>\S+)\s+(?<timestamp>\d{4}-\d{2}-\d{2}T\d{2}-\d{2}Z)\s+[A-Za-z0-9]+\.zip$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static int? TryExtractModId(string fileName)
    {
        var m = Pattern.Match(fileName);
        return m.Success && int.TryParse(m.Groups["modId"].Value, out var id) ? id : null;
    }

    public static string? TryExtractVersion(string fileName)
    {
        var m = Pattern.Match(fileName);
        return m.Success ? m.Groups["version"].Value.Trim() : null;
    }

    public static string? TryExtractModName(string fileName)
    {
        var m = Pattern.Match(fileName);
        return m.Success ? m.Groups["name"].Value.Trim() : null;
    }
}
