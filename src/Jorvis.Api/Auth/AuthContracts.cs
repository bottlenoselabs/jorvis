using System.Text.Json;

namespace Jorvis.Api.Auth;

/// <summary>The browser's <c>navigator.credentials.get()</c> result, serialized with <c>toJSON()</c>.</summary>
public sealed record SignInRequest(JsonElement? Credential);

public sealed record JoinOptionsRequest(string? Token, string? Email, string? Username);

/// <summary>The browser's <c>navigator.credentials.create()</c> result goes in <see cref="Credential"/>.</summary>
public sealed record JoinRequest(string? Token, string? Email, string? Username, JsonElement? Credential);
