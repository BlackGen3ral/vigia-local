using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vigia;

internal static class DesktopUpdate
{
    private const string Asset = "VigiaLocal-Setup.exe";
    private const string Repository = "BlackGen3ral/vigia-local";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(4) };

    static DesktopUpdate() => Client.DefaultRequestHeaders.UserAgent.ParseAdd("VigiaLocal-Windows/1.0");

    public static async Task<string?> Prepare()
    {
        using var document = JsonDocument.Parse(await Client.GetStringAsync(
            "https://api.github.com/repos/" + Repository + "/releases/latest"));
        var latest = document.RootElement;
        string tag = latest.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, "^v[0-9]+[.][0-9]+[.][0-9]+$") ||
            !Version.TryParse(tag[1..], out var version)) return null;
        var installed = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        if (version <= new Version(installed.Major, installed.Minor, installed.Build)) return null;

        string root = $"https://github.com/{Repository}/releases/download/{tag}/";
        var manifest = await Client.GetStringAsync(root + "SHA256SUMS.txt");
        var match = Regex.Match(manifest, @"(?m)^([a-fA-F0-9]{64})\s+VigiaLocal-Setup[.]exe\s*$");
        if (!match.Success) throw new InvalidDataException("Soma de verificação ausente.");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Vigia Local", "updates", tag);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, Asset);
        var temporary = destination + ".partial";
        try
        {
            using var response = await Client.GetAsync(root + Asset, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using (var output = File.Create(temporary))
                await response.Content.CopyToAsync(output);
            using (var input = File.OpenRead(temporary))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(input));
                if (!hash.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Instalador não corresponde à versão publicada.");
            }
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally { File.Delete(temporary); }
    }
}
