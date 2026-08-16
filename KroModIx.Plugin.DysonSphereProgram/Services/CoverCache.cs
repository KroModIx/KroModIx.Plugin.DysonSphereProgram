using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.DysonSphereProgram.Services;

/// <summary>Cover-Cache — SHA1(URL) als Key (stabil auch bei CDN-Subdomain-
/// Rotation, siehe Skill Kernprinzip).
/// <para>v0.2.1: Nexus liefert Cover als WebP (VP8) — Avalonias Bitmap-Ctor
/// kann kein WebP. Beim Cache-Write erkennt der Cache das WebP-Magic
/// (`RIFF....WEBP`) und konvertiert via ffmpeg zu PNG. Wenn ffmpeg fehlt,
/// wird die Rohdatei durchgereicht — Bitmap-Ctor scheitert, die Row zeigt
/// den Emoji-Fallback (kein Crash).</para></summary>
public sealed class CoverCache
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly HttpClient _http;
    private readonly IHostServices _host;
    private readonly string _dir;

    public CoverCache(HttpClient http, IHostServices host)
    {
        _http = http;
        _host = host;
        _dir = Path.Combine(host.PluginCacheDir, "nexus-covers");
        Directory.CreateDirectory(_dir);
    }

    public async Task<string?> GetOrDownloadCoverAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var path = Path.Combine(_dir, Sha1(url) + ".img");
        if (File.Exists(path) && new FileInfo(path).Length > 0) return path;
        try
        {
            using var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0) return null;

            // v0.2.1: WebP → PNG via ffmpeg (Avalonia kann kein WebP).
            if (IsWebP(bytes))
            {
                var converted = await TryConvertWithFfmpegAsync(bytes, ".webp");
                if (converted is not null) bytes = converted;
                // Fallback: Rohdaten durchreichen, Bitmap-Ctor scheitert
                // dann, Row zeigt Emoji.
            }

            var tmp = path + $".tmp.{Guid.NewGuid():N}";
            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        catch (Exception ex)
        {
            _host.Logger.Debug(ex, "Cover-Download fehlgeschlagen: {Url}", url);
            return null;
        }
    }

    /// <summary>WebP-Magic: <c>RIFF....WEBP</c> — Bytes 0-3 = "RIFF",
    /// Bytes 8-11 = "WEBP".</summary>
    private static bool IsWebP(byte[] b) =>
        b.Length >= 12
        && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
        && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50;

    /// <summary>Umweg ueber zwei Temp-Files statt stdin/stdout-Pipe — bei
    /// letzterer gab's im .NET-Pipe-Wrapper partial-Write-Bugs bei ~300 KB
    /// Files (siehe RenPyAssist CoverCache). Bei fehlendem ffmpeg: return
    /// null, Caller reicht die Rohdatei durch.</summary>
    private async Task<byte[]?> TryConvertWithFfmpegAsync(byte[] source, string inputExt)
    {
        var inPath = Path.Combine(Path.GetTempPath(), $"dsp-cover-{Guid.NewGuid():N}{inputExt}");
        var outPath = Path.Combine(Path.GetTempPath(), $"dsp-cover-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(inPath, source);
            var psi = new ProcessStartInfo("ffmpeg")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[] { "-y", "-nostdin", "-loglevel", "error",
                "-i", inPath, "-c:v", "png", outPath })
                psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var errTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0 || !File.Exists(outPath))
            {
                _host.Logger.Debug("ffmpeg Cover-Convert ({Ext}) exit={Code}, stderr={Err}",
                    inputExt, proc.ExitCode, await errTask);
                return null;
            }
            return await File.ReadAllBytesAsync(outPath);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // ffmpeg nicht im PATH — silently, Caller reicht Rohdatei durch
            _host.Logger.Debug("ffmpeg nicht installiert — Cover ({Ext}) kann nicht konvertiert werden", inputExt);
            return null;
        }
        catch (Exception ex)
        {
            _host.Logger.Debug(ex, "ffmpeg Cover-Convert-Ausnahme");
            return null;
        }
        finally
        {
            try { if (File.Exists(inPath)) File.Delete(inPath); } catch { }
            try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
        }
    }

    private static string Sha1(string s)
    {
        using var sha = SHA1.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes);
    }
}
