using System.Net.Http.Json;
using System.Text.Json;

namespace Jorvis.Web.Auth.Services;

public sealed record ApiResponse(bool Ok, string Body, string? Message);

/// <summary>Calls the API's public sign-in and join endpoints, which share this origin behind nginx.</summary>
public sealed class AuthApi(HttpClient http)
{
    private const string Fallback = "Something went wrong. Try again.";

    public async Task<ApiResponse> PostAsync(string path, object? body = null)
    {
        try
        {
            using var response = body is null
                ? await http.PostAsync(path, content: null)
                : await http.PostAsJsonAsync(path, body);
            var text = await response.Content.ReadAsStringAsync();
            return new ApiResponse(response.IsSuccessStatusCode, text, response.IsSuccessStatusCode ? null : MessageFor(text));
        }
        catch (HttpRequestException)
        {
            return new ApiResponse(false, "", "Could not reach the server. Try again.");
        }
    }

    private static string MessageFor(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var messages = errors.EnumerateObject()
                    .SelectMany(property => property.Value.EnumerateArray())
                    .Select(message => message.GetString())
                    .Where(message => !string.IsNullOrEmpty(message));
                var joined = string.Join(' ', messages);
                if (joined.Length > 0)
                    return joined;
            }

            if (root.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } text)
                return text;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
        }

        return Fallback;
    }
}
