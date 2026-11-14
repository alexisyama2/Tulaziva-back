using FluentValidation;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;

namespace Tulaziva.Application.Ads;

public static class FeaturingRules
{
    /// <summary>The durations that can be chosen, in days.</summary>
    public static readonly IReadOnlyList<int> Plans = new[] { 3, 7, 30 };

    /// <summary>However many times an ad is featured, it never stays featured for longer than this from now.</summary>
    public const int MaxDaysAhead = 90;
}

/// <summary>
/// Whether the owners can feature their own ads. There is no payment in the system yet, so by default only the admins can; a host that
/// takes the payment before calling the use case (or that wants free featuring, as in development) turns this on.
/// </summary>
public interface IFeaturingPolicy
{
    bool OwnersCanFeature { get; }
}

public sealed record FeaturingOptionsDto(bool OwnersCanFeature, IReadOnlyList<int> Plans);

public sealed record GetFeaturingOptionsQuery : IQuery<FeaturingOptionsDto>;

public sealed class GetFeaturingOptionsQueryHandler : IQueryHandler<GetFeaturingOptionsQuery, FeaturingOptionsDto>
{
    private readonly IFeaturingPolicy _policy;

    public GetFeaturingOptionsQueryHandler(IFeaturingPolicy policy)
    {
        _policy = policy;
    }

    public Task<FeaturingOptionsDto> HandleAsync(GetFeaturingOptionsQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new FeaturingOptionsDto(_policy.OwnersCanFeature, FeaturingRules.Plans));
}

// ---- Feature ----

/// <summary>
/// Shows the ad first in the searches for some days. Calling it again adds the days to the ones that are left.
/// The owner can do it when the policy allows it; an admin can always.
/// </summary>
public sealed record FeatureAdCommand(Guid UserId, bool IsAdmin, Guid AdId, int Days) : ICommand<AdDto>;

public sealed class FeatureAdCommandValidator : AbstractValidator<FeatureAdCommand>
{
    public FeatureAdCommandValidator(ILocalizer localizer)
    {
        RuleFor(x => x.Days)
            .Must(days => FeaturingRules.Plans.Contains(days))
            .WithMessage(_ => localizer.Get(MessageKeys.FeatureDaysInvalid, string.Join(", ", FeaturingRules.Plans)));
    }
}

public sealed class FeatureAdCommandHandler : ICommandHandler<FeatureAdCommand, AdDto>
{
    private readonly IAdRepository _ads;
    private readonly IFeaturingPolicy _policy;
    private readonly TimeProvider _time;

    public FeatureAdCommandHandler(IAdRepository ads, IFeaturingPolicy policy, TimeProvider time)
    {
        _ads = ads;
        _policy = policy;
        _time = time;
    }

    public async Task<AdDto> HandleAsync(FeatureAdCommand command, CancellationToken cancellationToken)
    {
        var ad = await AdAccess.GetAsync(_ads, command.AdId, cancellationToken);
        var now = _time.GetUtcNow().UtcDateTime;

        if (!command.IsAdmin)
        {
            AdAccess.RequireOwner(ad, command.UserId);

            if (!_policy.OwnersCanFeature)
                throw new ForbiddenException(MessageKeys.FeaturingUnavailable);
        }

        // Only an ad the public can see is worth featuring.
        if (!AdAccess.CanView(ad, Requester.Anonymous, now))
            throw new ConflictException(MessageKeys.AdNotPublic);

        var from = ad.FeaturedUntilUtc is { } current && current > now ? current : now;
        var until = from.AddDays(command.Days);
        var limit = now.AddDays(FeaturingRules.MaxDaysAhead);

        ad.FeaturedUntilUtc = until > limit ? limit : until;
        ad.UpdatedAtUtc = now;

        await _ads.SaveChangesAsync(cancellationToken);

        return AdMapper.ToDto(ad);
    }
}

// ---- Stop featuring ----

/// <summary>Takes the ad out of the featured ones right away. The owner or an admin can do it.</summary>
public sealed record UnfeatureAdCommand(Guid UserId, bool IsAdmin, Guid AdId) : ICommand<AdDto>;

public sealed class UnfeatureAdCommandHandler : ICommandHandler<UnfeatureAdCommand, AdDto>
{
    private readonly IAdRepository _ads;
    private readonly TimeProvider _time;

    public UnfeatureAdCommandHandler(IAdRepository ads, TimeProvider time)
    {
        _ads = ads;
        _time = time;
    }

    public async Task<AdDto> HandleAsync(UnfeatureAdCommand command, CancellationToken cancellationToken)
    {
        var ad = await AdAccess.GetAsync(_ads, command.AdId, cancellationToken);

        if (!command.IsAdmin)
            AdAccess.RequireOwner(ad, command.UserId);

        ad.FeaturedUntilUtc = null;
        ad.UpdatedAtUtc = _time.GetUtcNow().UtcDateTime;

        await _ads.SaveChangesAsync(cancellationToken);

        return AdMapper.ToDto(ad);
    }
}
