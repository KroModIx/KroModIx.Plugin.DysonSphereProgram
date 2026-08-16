using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.DysonSphereProgram.Services;

/// <summary>Cover-Cache — SHA1(URL) als Key (stabil auch bei CDN-Subdomain-
/// Rotation, siehe Skill Kernprinzip). Analog Cyberpunk-Plugin.</summary>
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

    private static string Sha1(string s)
    {
        using var sha = SHA1.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes);
    }
}
