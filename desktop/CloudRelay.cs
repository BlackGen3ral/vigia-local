using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace Vigia;

internal static class CloudRelay
{
    public const string Origin = "https://vigia-local.pages.dev";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static string NewWriter() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public static string Invite(string writer)
    {
        var reader = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("read:" + writer)));
        return Origin + "/?app#pair=" + reader;
    }

    public static Task Activate(string writer, string enrollment) => Send("activate", writer, enrollment: enrollment);
    public static Task Heartbeat(string writer) => Send("heartbeat", writer);
    public static Task Arrival(string writer, string kind) => Send("events", writer, new { id = Guid.NewGuid().ToString(), kind });

    private static async Task Send(string path, string writer, object? payload = null, string? enrollment = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Origin + "/api/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", writer);
        if (enrollment is not null) request.Headers.Add("X-Vigia-Enrollment", enrollment);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        using var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
