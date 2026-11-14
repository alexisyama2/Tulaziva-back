namespace Tulaziva.Domain.Entities;

/// <summary>An ad a user saved to see it later. The pair (user, ad) is the key: an ad can be saved once per user.</summary>
public class Favorite
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid AdId { get; set; }
    public Ad Ad { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
