using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Jorvis.Api.Auth;
using Jorvis.Core.Contracts;
using Jorvis.Infrastructure.Data;
using Jorvis.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jorvis.Api.Tests;

public sealed class AuthTests : IDisposable
{
    private const string Email = "friend@example.com";
    private const string Username = "friend";

    private readonly ApiFactory _factory = new();
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();
        _factory.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A separate browser: its own cookie jar.</summary>
    private HttpClient NewBrowser()
    {
        var client = _factory.CreateClient();
        _disposables.Add(client);
        return client;
    }

    private TestAuthenticator NewPhone()
    {
        var phone = new TestAuthenticator();
        _disposables.Add(phone);
        return phone;
    }

    private static async Task<(HttpResponseMessage Options, HttpResponseMessage? Joined)> JoinAsync(
        HttpClient browser, TestAuthenticator phone, string? token, string email = Email, string? username = Username)
    {
        var options = await browser.PostAsJsonAsync("/api/auth/join/start", new JoinOptionsRequest(token, email, username), Ct);
        if (!options.IsSuccessStatusCode)
            return (options, null);

        var credential = phone.Create(await options.Content.ReadAsStringAsync(Ct));
        var joined = await browser.PostAsJsonAsync("/api/auth/join/finish", new { token, email, username, credential }, Ct);
        return (options, joined);
    }

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient browser, TestAuthenticator phone, Action<JsonObject>? tamper = null)
    {
        var options = await browser.PostAsync("/api/auth/sign-in/start", null, Ct);
        options.EnsureSuccessStatusCode();
        var credential = phone.Get(await options.Content.ReadAsStringAsync(Ct));
        tamper?.Invoke(credential);
        return await browser.PostAsJsonAsync("/api/auth/sign-in/finish", new { credential }, Ct);
    }

    private async Task<int> PasskeyCountAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(email);
        return user is null ? 0 : (await users.GetPasskeysAsync(user)).Count;
    }

    [Fact]
    public async Task Health_IsPublic()
    {
        var response = await NewBrowser().GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/projects")]
    [InlineData("POST", "/api/projects")]
    [InlineData("GET", "/api/projects/some-project/tasks")]
    [InlineData("GET", "/api/invitations")]
    [InlineData("GET", "/api/auth/session")]
    [InlineData("POST", "/api/auth/sign-out")]
    public async Task ProtectedEndpoints_RejectAnonymousRequests(string method, string path)
    {
        var response = await NewBrowser().SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Join_WithValidInvite_CreatesAccountAndSignsIn()
    {
        var browser = NewBrowser();

        var (_, joined) = await JoinAsync(browser, NewPhone(), await _factory.CreateInviteAsync());

        Assert.Equal(HttpStatusCode.Created, joined?.StatusCode);
        var cookie = Assert.Single(joined!.Headers.GetValues("Set-Cookie"), c => c.StartsWith("jorvis.auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        var session = await browser.GetFromJsonAsync<SessionResponse>("/api/auth/session", Ct);
        Assert.Equal(Email, session?.Email);
        Assert.Equal(Username, session?.UserName);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/projects", Ct)).StatusCode);
        Assert.Equal(1, await PasskeyCountAsync(Email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("not@allowed")]
    [InlineData("this-username-is-way-too-long-to-be-accepted")]
    public async Task Join_WithAnInvalidUsername_IsRejected(string? username)
    {
        var (options, _) = await JoinAsync(NewBrowser(), NewPhone(), await _factory.CreateInviteAsync(), username: username);

        Assert.Equal(HttpStatusCode.BadRequest, options.StatusCode);
        Assert.Equal(0, await PasskeyCountAsync(Email));
    }

    [Theory]
    [InlineData("friend")]
    [InlineData("FRIEND")]
    public async Task Join_WithATakenUsername_IsRejected_IgnoringCase(string username)
    {
        await JoinAsync(NewBrowser(), NewPhone(), await _factory.CreateInviteAsync());

        var (options, _) = await JoinAsync(NewBrowser(), NewPhone(), await _factory.CreateInviteAsync(), "second@example.com", username);

        Assert.Equal(HttpStatusCode.BadRequest, options.StatusCode);
        Assert.Contains(Usernames.Taken, await options.Content.ReadAsStringAsync(Ct));
        Assert.Equal(0, await PasskeyCountAsync("second@example.com"));
    }

    [Fact]
    public async Task Recover_KeepsTheExistingUsername()
    {
        await JoinAsync(NewBrowser(), NewPhone(), await _factory.CreateInviteAsync());
        var recovery = await _factory.CreateInviteAsync(email: Email, purpose: InvitePurpose.Recover);

        var browser = NewBrowser();
        var (_, joined) = await JoinAsync(browser, NewPhone(), recovery, username: null);

        Assert.Equal(HttpStatusCode.Created, joined?.StatusCode);
        Assert.Equal(Username, (await browser.GetFromJsonAsync<SessionResponse>("/api/auth/session", Ct))?.UserName);
    }

    [Fact]
    public async Task Join_NeverStoresAPassword()
    {
        await JoinAsync(NewBrowser(), NewPhone(), await _factory.CreateInviteAsync());

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(Email);
        Assert.NotNull(user);
        Assert.False(await users.HasPasswordAsync(user));
    }

    [Fact]
    public async Task Join_WithoutValidInvite_IsRejected()
    {
        var (options, _) = await JoinAsync(NewBrowser(), NewPhone(), "not-a-real-token");

        Assert.Equal(HttpStatusCode.BadRequest, options.StatusCode);
        Assert.Equal(0, await PasskeyCountAsync(Email));
    }

    [Fact]
    public async Task Invite_CanBeRedeemedOnlyOnce()
    {
        var token = await _factory.CreateInviteAsync();

        var first = await JoinAsync(NewBrowser(), NewPhone(), token);
        var second = await JoinAsync(NewBrowser(), NewPhone(), token, "second@example.com", "second");

        Assert.Equal(HttpStatusCode.Created, first.Joined?.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.Options.StatusCode);
    }

    [Fact]
    public async Task Invite_Expired_IsRejected()
    {
        var token = await _factory.CreateInviteAsync(lifetime: TimeSpan.FromSeconds(-1));

        var (options, _) = await JoinAsync(NewBrowser(), NewPhone(), token);

        Assert.Equal(HttpStatusCode.BadRequest, options.StatusCode);
    }

    [Fact]
    public async Task Invite_BoundToEmail_RejectsOtherAddresses()
    {
        var token = await _factory.CreateInviteAsync(email: "intended@example.com");

        var wrong = await JoinAsync(NewBrowser(), NewPhone(), token, "someone-else@example.com");
        var right = await JoinAsync(NewBrowser(), NewPhone(), token, "Intended@Example.com", "intended");

        Assert.Equal(HttpStatusCode.BadRequest, wrong.Options.StatusCode);
        Assert.Equal(HttpStatusCode.Created, right.Joined?.StatusCode);
    }

    [Fact]
    public async Task Join_ForAnExistingEmail_IsRejected()
    {
        await JoinAsync(NewBrowser(), NewPhone(), await _factory.CreateInviteAsync());

        var (options, _) = await JoinAsync(NewBrowser(), NewPhone(), await _factory.CreateInviteAsync());

        Assert.Equal(HttpStatusCode.BadRequest, options.StatusCode);
    }

    [Fact]
    public async Task Join_WithoutRequestingChallengeFirst_IsRejected()
    {
        var browser = NewBrowser();
        var token = await _factory.CreateInviteAsync();
        var forged = NewPhone().Create("""{"challenge":"AAAAAAAAAAAAAAAAAAAAAA","user":{"id":"AAAA"}}""");

        var response = await browser.PostAsJsonAsync("/api/auth/join/finish", new { token, email = Email, username = Username, credential = forged }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await PasskeyCountAsync(Email));
    }

    [Fact]
    public async Task Join_PasskeyMadeForAnotherEmail_IsRejectedAndKeepsTheInvite()
    {
        var browser = NewBrowser();
        var phone = NewPhone();
        var token = await _factory.CreateInviteAsync();

        // The challenge was issued for one address, but the join claims another.
        var options = await browser.PostAsJsonAsync("/api/auth/join/start", new JoinOptionsRequest(token, Email, Username), Ct);
        var credential = phone.Create(await options.Content.ReadAsStringAsync(Ct));
        var mismatched = await browser.PostAsJsonAsync("/api/auth/join/finish", new { token, email = "other@example.com", username = Username, credential }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, mismatched.StatusCode);
        var retry = await JoinAsync(NewBrowser(), NewPhone(), token);
        Assert.Equal(HttpStatusCode.Created, retry.Joined?.StatusCode);
    }

    [Fact]
    public async Task SignIn_WithRegisteredPasskey_Works()
    {
        var phone = NewPhone();
        await JoinAsync(NewBrowser(), phone, await _factory.CreateInviteAsync());
        var laterBrowser = NewBrowser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await laterBrowser.GetAsync("/api/projects", Ct)).StatusCode);

        var response = await SignInAsync(laterBrowser, phone);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Email, (await response.Content.ReadFromJsonAsync<SessionResponse>(Ct))?.Email);
        Assert.Equal(HttpStatusCode.OK, (await laterBrowser.GetAsync("/api/projects", Ct)).StatusCode);
    }

    [Fact]
    public async Task SignIn_WithAPasskeyTheServerHasNeverSeen_IsRejected()
    {
        await JoinAsync(NewBrowser(), NewPhone(), await _factory.CreateInviteAsync());

        var response = await SignInAsync(NewBrowser(), NewPhone());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SignIn_WithATamperedSignature_IsRejected()
    {
        var phone = NewPhone();
        await JoinAsync(NewBrowser(), phone, await _factory.CreateInviteAsync());

        var browser = NewBrowser();
        var response = await SignInAsync(browser, phone, credential =>
        {
            var signature = Base64UrlTextEncoder.Decode(credential["response"]!["signature"]!.GetValue<string>());
            signature[^1] ^= 0xFF;
            credential["response"]!["signature"] = Base64UrlTextEncoder.Encode(signature);
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/projects", Ct)).StatusCode);
    }

    [Fact]
    public async Task SignIn_WithoutRequestingChallengeFirst_IsRejected()
    {
        var phone = NewPhone();
        await JoinAsync(NewBrowser(), phone, await _factory.CreateInviteAsync());
        var credential = phone.Get("""{"challenge":"AAAAAAAAAAAAAAAAAAAAAA"}""");

        var response = await NewBrowser().PostAsJsonAsync("/api/auth/sign-in/finish", new { credential }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SignOut_EndsTheSession()
    {
        var browser = NewBrowser();
        await JoinAsync(browser, NewPhone(), await _factory.CreateInviteAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("/api/auth/sign-out", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/projects", Ct)).StatusCode);
    }

    [Fact]
    public async Task Recover_ReplacesEveryOldPasskey()
    {
        var oldPhone = NewPhone();
        var newPhone = NewPhone();
        await JoinAsync(NewBrowser(), oldPhone, await _factory.CreateInviteAsync());
        var recovery = await _factory.CreateInviteAsync(email: Email, purpose: InvitePurpose.Recover);

        var recoveryBrowser = NewBrowser();
        var (_, joined) = await JoinAsync(recoveryBrowser, newPhone, recovery);

        Assert.Equal(HttpStatusCode.Created, joined?.StatusCode);
        Assert.Equal(1, await PasskeyCountAsync(Email));
        Assert.Equal(HttpStatusCode.OK, (await recoveryBrowser.GetAsync("/api/projects", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(NewBrowser(), oldPhone)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(NewBrowser(), newPhone)).StatusCode);
    }

    [Fact]
    public async Task Recover_EndsSessionsOnOtherDevices()
    {
        // In production sessions are rechecked about once a minute; check on every request here.
        _factory.ConfigureTestServices = services =>
            services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
        var stolenBrowser = NewBrowser();
        await JoinAsync(stolenBrowser, NewPhone(), await _factory.CreateInviteAsync());
        Assert.Equal(HttpStatusCode.OK, (await stolenBrowser.GetAsync("/api/projects", Ct)).StatusCode);

        await JoinAsync(NewBrowser(), NewPhone(), await _factory.CreateInviteAsync(email: Email, purpose: InvitePurpose.Recover));

        Assert.Equal(HttpStatusCode.Unauthorized, (await stolenBrowser.GetAsync("/api/projects", Ct)).StatusCode);
    }

    [Fact]
    public async Task Recover_ForAnUnknownEmail_IsRejected()
    {
        var token = await _factory.CreateInviteAsync(email: "nobody@example.com", purpose: InvitePurpose.Recover);

        var (options, _) = await JoinAsync(NewBrowser(), NewPhone(), token, "nobody@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, options.StatusCode);
    }

    [Fact]
    public async Task Ceremonies_AreRateLimited()
    {
        _factory.Settings["Auth:RateLimitPerMinute"] = "10";
        var browser = NewBrowser();

        HttpStatusCode last = default;
        for (var i = 0; i < 12; i++)
            last = (await browser.PostAsync("/api/auth/sign-in/start", null, Ct)).StatusCode;

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Theory]
    [InlineData("cross-site", "https://evil.example")]
    [InlineData("same-site", "https://evil.example")]
    public async Task StateChangingRequests_FromOtherSites_AreRejected(string fetchSite, string origin)
    {
        var browser = NewBrowser();
        await _factory.SignInAsync(browser);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/projects")
        {
            Content = JsonContent.Create(new CreateProjectRequest("From another site", "from-another-site"))
        };
        request.Headers.Add("Sec-Fetch-Site", fetchSite);
        request.Headers.Add("Origin", origin);

        Assert.Equal(HttpStatusCode.Forbidden, (await browser.SendAsync(request, Ct)).StatusCode);
    }

    [Fact]
    public async Task StateChangingRequests_FromSameOrigin_AreAllowed()
    {
        var browser = NewBrowser();
        await _factory.SignInAsync(browser);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/projects")
        {
            Content = JsonContent.Create(new CreateProjectRequest("From the app", "from-the-app"))
        };
        request.Headers.Add("Sec-Fetch-Site", "same-origin");

        Assert.Equal(HttpStatusCode.Created, (await browser.SendAsync(request, Ct)).StatusCode);
    }
}
