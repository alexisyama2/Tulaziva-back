using Tulaziva.Application.Common;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Application.Ads;

/// <summary>The rules of who can see and change an ad, shared by the handlers.</summary>
internal static class AdAccess
{
    /// <summary>An ad is public while it is active and not expired. Its owner and the admins can always see it.</summary>
    public static bool CanView(Ad ad, Requester requester, DateTime nowUtc) =>
        (ad.Status == AdStatus.Active && ad.ExpiresAtUtc > nowUtc) || requester.IsAdmin || requester.Owns(ad);

    public static async Task<Ad> GetAsync(IAdRepository ads, Guid adId, CancellationToken cancellationToken) =>
        await ads.GetByIdAsync(adId, cancellationToken) ?? throw new NotFoundException(MessageKeys.AdNotFound);

    /// <summary>Only the owner changes an ad. Admins take ads down, they do not edit them.</summary>
    public static Ad RequireOwner(Ad ad, Guid userId)
    {
        if (ad.OwnerId != userId)
            throw new ForbiddenException(MessageKeys.Forbidden);

        return ad;
    }

    public static void RequireNotRemoved(Ad ad)
    {
        if (ad.Status == AdStatus.Removed)
            throw new ConflictException(MessageKeys.AdRemoved);
    }
}
