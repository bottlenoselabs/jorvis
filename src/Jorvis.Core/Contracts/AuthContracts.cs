namespace Jorvis.Core.Contracts;

/// <summary>Who is signed in. Display only: the server decides what each request may do.</summary>
public sealed record SessionResponse(string UserName, string Email);
