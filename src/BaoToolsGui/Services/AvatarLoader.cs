using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BaoToolsGui.Services;

/// <summary>
/// Normalizes and loads Discord / remote user avatars reliably into WPF BitmapImage.
/// Discord avatar URLs can return WebP by default, which standard WPF image components
/// cannot decode natively. This helper forces PNG format and caches the decoded images.
/// </summary>
public static class AvatarLoader
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new();

    /// <summary>
    /// Normalizes Discord avatar URLs to PNG format and adds size parameter.
    /// Works for Discord CDN avatars, embed avatars, and general URLs.
    /// </summary>
    public static string? NormalizeDiscordAvatarUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            if (url.Contains("cdn.discordapp.com/avatars/") || url.Contains("cdn.discordapp.com/embed/avatars/"))
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath;
                var ext = Path.GetExtension(path);

                if (string.IsNullOrEmpty(ext))
                {
                    path += ".png";
                }
                else if (!ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
                {
                    path = Path.ChangeExtension(path, ".png");
                }

                return $"https://{uri.Host}{path}?size=128";
            }
        }
        catch
        {
            // Fall back to original url if parsing fails
        }

        return url;
    }

    /// <summary>
    /// Fetches the avatar image from the URL and converts it into a frozen BitmapImage.
    /// Returns null if download fails or URL is empty.
    /// </summary>
    public static async Task<ImageSource?> LoadAvatarAsync(string? rawUrl)
    {
        var url = NormalizeDiscordAvatarUrl(rawUrl);
        if (string.IsNullOrWhiteSpace(url)) return null;

        if (Cache.TryGetValue(url, out var cached))
            return cached;

        try
        {
            using var client = AppHttp.Create(TimeSpan.FromSeconds(10));
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko)");

            using var resp = await client.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            byte[] bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0) return null;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();

            Cache[url] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Clears the memory cache (e.g. on user sign out).</summary>
    public static void ClearCache() => Cache.Clear();
}
