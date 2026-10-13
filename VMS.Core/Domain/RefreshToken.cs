namespace VMS.Core.Domain;

/// <summary>
/// A long-lived credential that lets the client silently obtain a new (short-lived) JWT without
/// forcing a full re-login. Only the SHA-256 hash of the raw token is ever persisted — same
/// reasoning as PasswordHash — so a leaked database dump doesn't hand out usable sessions.
/// Rotated on every use (see AuthEndpoints' /api/auth/refresh): each row is single-use, replaced
/// by a fresh row on its next successful refresh, and the old one is marked RevokedAt rather than
/// deleted, so a replayed/stolen old token is detectably rejected instead of silently working.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
