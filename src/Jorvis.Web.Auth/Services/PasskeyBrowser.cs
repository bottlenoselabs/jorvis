using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Jorvis.Web.Auth.Services;

/// <summary>Credential is set on success and Error (the browser's exception name) on failure.</summary>
public sealed record PasskeyResult(JsonElement? Credential, string? Error);

/// <summary>The browser's WebAuthn API can only be called from JavaScript, so this wraps the small shim in passkeys.js.</summary>
public sealed class PasskeyBrowser(IJSRuntime js, NavigationManager navigation) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async Task<bool> IsSupportedAsync() =>
        await (await ModuleAsync()).InvokeAsync<bool>("isSupported");

    public Task<PasskeyResult> CreateAsync(string optionsJson) => RunAsync("create", optionsJson);

    public Task<PasskeyResult> GetAsync(string optionsJson) => RunAsync("get", optionsJson);

    /// <summary>Takes the invite token out of the address bar so it does not linger in the history.</summary>
    public async Task ClearFragmentAsync() =>
        await (await ModuleAsync()).InvokeVoidAsync("clearFragment");

    public static string Describe(string? error) => error switch
    {
        "NotAllowedError" => "The request was cancelled or timed out.",
        "InvalidStateError" => "This device is already set up for that account.",
        "SecurityError" => "This only works at the address Jorvis was set up with. Open the link you were given.",
        _ => "That didn't work. Try again."
    };

    private async Task<PasskeyResult> RunAsync(string function, string optionsJson)
    {
        var json = await (await ModuleAsync()).InvokeAsync<string>(function, optionsJson);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("credential", out var credential)
            ? new PasskeyResult(credential.Clone(), null)
            : new PasskeyResult(null, document.RootElement.GetProperty("error").GetString());
    }

    private async ValueTask<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", navigation.ToAbsoluteUri("passkeys.js").ToString());

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
            await _module.DisposeAsync();
    }
}
