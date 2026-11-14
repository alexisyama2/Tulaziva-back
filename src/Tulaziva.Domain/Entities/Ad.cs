using Tulaziva.Domain.Enums;

namespace Tulaziva.Domain.Entities;

public class Ad
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal Price { get; set; }

    /// <summary>ISO 4217 code (MXN, USD...).</summary>
    public string Currency { get; set; } = "MXN";

    /// <summary>Where the item is. Used to search by distance. Null when neither the ad nor its owner has a location.</summary>
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public AdStatus Status { get; set; } = AdStatus.Active;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>After this moment the ad is no longer shown in searches. The owner can renew it.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>While this moment is in the future the ad is shown first in the searches. Null when it was never featured.</summary>
    public DateTime? FeaturedUntilUtc { get; set; }

    public List<AdImage> Images { get; set; } = new();
}
