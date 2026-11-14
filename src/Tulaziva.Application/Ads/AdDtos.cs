using Tulaziva.Domain.Entities;

namespace Tulaziva.Application.Ads;

public sealed record AdImageDto(Guid Id, int Position, string ContentType, long SizeBytes, string Url);

public sealed record AdDto(
    Guid Id,
    Guid OwnerId,
    string OwnerDisplayName,
    string? OwnerPhotoUrl,
    int CategoryId,
    string Title,
    string Description,
    decimal Price,
    string Currency,
    double? Latitude,
    double? Longitude,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime ExpiresAtUtc,
    IReadOnlyList<AdImageDto> Images,
    DateTime? FeaturedUntilUtc = null);

/// <summary>The short form used in lists.</summary>
public sealed record AdSummaryDto(
    Guid Id,
    int CategoryId,
    string Title,
    decimal Price,
    string Currency,
    double? Latitude,
    double? Longitude,
    double? DistanceKm,
    string? CoverImageUrl,
    DateTime CreatedAtUtc,
    DateTime? FeaturedUntilUtc = null);

public sealed record CategoryDto(int Id, string Key, string Name);

public sealed record AdImageFile(Stream Content, string ContentType);

/// <summary>Who is asking: nobody (anonymous), a user, or an admin.</summary>
public sealed record Requester(Guid? UserId, bool IsAdmin = false)
{
    public static Requester Anonymous { get; } = new(UserId: null);

    public bool Owns(Ad ad) => UserId.HasValue && UserId.Value == ad.OwnerId;
}

public static class AdRoutes
{
    public static string Image(Guid adId, Guid imageId) => $"/api/ads/{adId}/images/{imageId}";
}

internal static class AdMapper
{
    public static AdImageDto ToDto(AdImage image) =>
        new(image.Id, image.Position, image.ContentType, image.SizeBytes, AdRoutes.Image(image.AdId, image.Id));

    public static AdDto ToDto(Ad ad) => new(
        ad.Id,
        ad.OwnerId,
        ad.Owner?.DisplayName ?? string.Empty,
        ad.Owner == null ? null : Users.UserRoutes.Photo(ad.Owner),
        ad.CategoryId,
        ad.Title,
        ad.Description,
        ad.Price,
        ad.Currency,
        ad.Latitude,
        ad.Longitude,
        ad.Status.ToString(),
        ad.CreatedAtUtc,
        ad.UpdatedAtUtc,
        ad.ExpiresAtUtc,
        ad.Images.OrderBy(i => i.Position).Select(ToDto).ToList(),
        ad.FeaturedUntilUtc);

    public static AdSummaryDto ToSummary(Ad ad, double? distanceKm = null)
    {
        var cover = ad.Images.OrderBy(i => i.Position).FirstOrDefault();

        return new AdSummaryDto(
            ad.Id,
            ad.CategoryId,
            ad.Title,
            ad.Price,
            ad.Currency,
            ad.Latitude,
            ad.Longitude,
            distanceKm.HasValue ? Math.Round(distanceKm.Value, 1) : null,
            cover == null ? null : AdRoutes.Image(ad.Id, cover.Id),
            ad.CreatedAtUtc,
            ad.FeaturedUntilUtc);
    }
}
