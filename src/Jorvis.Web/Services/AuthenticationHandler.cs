using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Jorvis.Web.Services;

/// <summary>
/// The browser holds the session in an HttpOnly cookie this code cannot read, and the server decides what each request
/// may do. All this does is send the cookie along and bounce to the sign-in page when the server says there's no session.
/// </summary>
public class AuthenticationHandler(NavigationManager navigation) : DelegatingHandler
{
    public const string SignInPath = "auth/sign-in";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            navigation.NavigateTo(SignInPath, forceLoad: true);

        return response;
    }
}
