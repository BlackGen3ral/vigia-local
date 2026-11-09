using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Vigia;

internal sealed record CameraSettings(string Host, string Port, string Channel, string Login, string Password, DoorZone? Zone, EntryLine? Line = null);
internal sealed record LocalSettings(string? Writer = null, CameraSettings? Camera = null, bool IncludeThumbnail = false)
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vigia Local");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.dat");

    public static LocalSettings Load()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            var data = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<LocalSettings>(data) ?? new();
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        var data = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(this), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath + ".tmp", data);
        File.Move(FilePath + ".tmp", FilePath, overwrite: true);
    }
}
