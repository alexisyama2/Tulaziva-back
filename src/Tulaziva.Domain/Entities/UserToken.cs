using Tulaziva.Domain.Enums;

namespace Tulaziva.Domain.Entities;

/// <summary>
/// A single-use secret sent by email (to reset a password or to verify the address). Only its SHA-256 hash is stored, so a
/// copy of the database does not give anyone a working link.
/// </summary>
public class UserToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public TokenPurpose Purpose { get; set; }

    /// <summary>Hex SHA-256 of the secret that was sent.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>When it was used, or replaced by a newer one. A token with a value here is dead.</summary>
    public DateTime? UsedAtUtc { get; set; }

    /// <summary>Data the token carries: the new address in an email change. Null for the other purposes.</summary>
    public string? Payload { get; set; }
}
