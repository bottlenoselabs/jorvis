using System.Net.Http.Json;
using Jorvis.Core.Contracts;

namespace Jorvis.Web.Services;

public class AuthApiClient(HttpClient http)
{
    public async Task<SessionResponse?> GetSessionAsync() =>
        await http.GetFromJsonAsync<SessionResponse>("api/auth/session");

    public async Task SignOutAsync()
    {
        var response = await http.PostAsync("api/auth/sign-out", content: null);
        response.EnsureSuccessStatusCode();
    }
}
