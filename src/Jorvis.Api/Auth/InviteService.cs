using System.Security.Cryptography;
using System.Text;
using Jorvis.Infrastructure.Data;
using Jorvis.Infrastructure.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Jorvis.Api.Auth;

public sealed class InviteService(JorvisDatabaseContext db, TimeProvider time)
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(72);

    /// <summary>Creates an invite and returns the secret token. The token is never stored, only its hash.</summary>
    public async Task<string> CreateAsync(
        string? email, TimeSpan lifetime, CancellationToken ct, InvitePurpose purpose = InvitePurpose.Join)
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = time.GetUtcNow();
        db.Invites.Add(new Invite
        {
            TokenHash = HashToken(token),
            Purpose = purpose,
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            CreatedAt = now,
            ExpiresAt = now + lifetime
        });
        await db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<Invite?> FindRedeemableAsync(string token, CancellationToken ct)
    {
        var hash = HashToken(token);
        var invite = await db.Invites.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);

        // Checked here rather than in the query: the hash is unique, and not every database can compare dates in SQL.
        return invite is { UsedAt: null } && invite.ExpiresAt > time.GetUtcNow() ? invite : null;
    }

    public DateTimeOffset UtcNow => time.GetUtcNow();

    private static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
