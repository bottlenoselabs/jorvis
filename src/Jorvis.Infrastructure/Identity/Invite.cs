namespace Jorvis.Infrastructure.Identity;

public enum InvitePurpose
{
    /// <summary>Create a new account with its first passkey.</summary>
    Join = 0,

    /// <summary>Replace every passkey on an existing account (lost or stolen device).</summary>
    Recover = 1
}

/// <summary>A single-use, expiring invitation to create or recover an account. Only a hash of the secret token is stored.</summary>
public class Invite
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string TokenHash { get; set; }

    public InvitePurpose Purpose { get; set; } = InvitePurpose.Join;

    /// <summary>When set, the invite can only be redeemed for this email address. Always set for recovery.</summary>
    public string? Email { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set once redeemed. Doubles as the concurrency token so an invite can only be claimed once.</summary>
    public DateTimeOffset? UsedAt { get; set; }
}
