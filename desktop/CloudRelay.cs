using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace Vigia;

internal static class CloudRelay
{
    public const string Origin = "https://vigia-local.pages.dev";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static string NewWriter() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public static async Task<string> Invite(string writer)
    {
        using var response = await Post("invite", writer);
        var invite = await response.Content.ReadFromJsonAsync<InviteResponse>();
        return invite?.Code ?? throw new InvalidOperationException("Código não recebido.");
    }
    private sealed record InviteResponse(string Code);

    public static Task Activate(string writer, string enrollment) => Send("activate", writer, enrollment: enrollment);
    public static Task Heartbeat(string writer) => Send("heartbeat", writer);
    public static Task Arrival(string writer, CameraArrival entry) => Send("events", writer, new
    {
        id = entry.Id,
        kind = "arrival",
        jpeg = entry.Thumbnail is null ? null : Convert.ToBase64String(entry.Thumbnail)
    });

    internal sealed record PushStatus(int Subscribed, int Accepted);
    internal sealed record TestResult(bool Accepted, PushStatus? Push);

    public static async Task<TestResult?> Test(string writer)
    {
        using var response = await Post("events", writer, new { id = Guid.NewGuid().ToString(), kind = "test" });
        return await response.Content.ReadFromJsonAsync<TestResult>();
    }

    private static async Task Send(string path, string writer, object? payload = null, string? enrollment = null)
    {
        using var response = await Post(path, writer, payload, enrollment);
    }

    private static async Task<HttpResponseMessage> Post(string path, string writer, object? payload = null, string? enrollment = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Origin + "/api/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", writer);
        if (enrollment is not null) request.Headers.Add("X-Vigia-Enrollment", enrollment);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        var response = await Client.SendAsync(request);
        if (response.IsSuccessStatusCode) return response;
        using (response) response.EnsureSuccessStatusCode();
        throw new InvalidOperationException("Resposta inesperada.");
    }
}
