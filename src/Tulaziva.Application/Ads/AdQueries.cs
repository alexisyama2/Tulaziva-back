using FluentValidation;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;

namespace Tulaziva.Application.Ads;

// ---- One ad ----

public sealed record GetAdQuery(Guid AdId, Requester Requester) : IQuery<AdDto>;

public sealed class GetAdQueryHandler : IQueryHandler<GetAdQuery, AdDto>
{
    private readonly IAdRepository _ads;
    private readonly TimeProvider _time;

    public GetAdQueryHandler(IAdRepository ads, TimeProvider time)
    {
        _ads = ads;
        _time = time;
    }

    public async Task<AdDto> HandleAsync(GetAdQuery query, CancellationToken cancellationToken)
    {
        var ad = await AdAccess.GetAsync(_ads, query.AdId, cancellationToken);

        // An ad nobody may see answers "not found", the same as one that does not exist.
        if (!AdAccess.CanView(ad, query.Requester, _time.GetUtcNow().UtcDateTime))
            throw new NotFoundException(MessageKeys.AdNotFound);

        return AdMapper.ToDto(ad);
    }
}

// ---- Ads of the signed-in user ----

public sealed record GetMyAdsQuery(Guid UserId, int Page = 1, int PageSize = 20) : IQuery<PagedResult<AdSummaryDto>>;

public sealed class GetMyAdsQueryValidator : AbstractValidator<GetMyAdsQuery>
{
    public GetMyAdsQueryValidator(ILocalizer localizer)
    {
        this.AddPagingRules(localizer, x => x.Page, x => x.PageSize);
    }
}

public sealed class GetMyAdsQueryHandler : IQueryHandler<GetMyAdsQuery, PagedResult<AdSummaryDto>>
{
    private readonly IAdRepository _ads;

    public GetMyAdsQueryHandler(IAdRepository ads)
    {
        _ads = ads;
    }

    public async Task<PagedResult<AdSummaryDto>> HandleAsync(GetMyAdsQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _ads.GetByOwnerAsync(query.UserId, query.Page, query.PageSize, cancellationToken);

        return new PagedResult<AdSummaryDto>(items.Select(ad => AdMapper.ToSummary(ad)).ToList(), query.Page, query.PageSize, totalCount);
    }
}

// ---- Search ----

/// <summary>
/// Search of the public ads. With latitude and longitude the nearest come first and each one carries its distance;
/// with a radius the farther ones are left out.
/// </summary>
public sealed record SearchAdsQuery(
    string? Text = null,
    int? CategoryId = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    double? Latitude = null,
    double? Longitude = null,
    double? RadiusKm = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<AdSummaryDto>>;

public sealed class SearchAdsQueryValidator : AbstractValidator<SearchAdsQuery>
{
    public SearchAdsQueryValidator(ILocalizer localizer)
    {
        RuleFor(x => x.Text)
            .MaximumLength(AdRules.MaxSearchTextLength).WithMessage(_ => localizer.Get(MessageKeys.SearchTextTooLong, AdRules.MaxSearchTextLength));

        RuleFor(x => x)
            .Must(q => (q.MinPrice ?? 0) >= 0
                       && (q.MaxPrice ?? 0) >= 0
                       && (!q.MinPrice.HasValue || !q.MaxPrice.HasValue || q.MinPrice <= q.MaxPrice))
            .WithMessage(_ => localizer.Get(MessageKeys.PriceRangeInvalid))
            .OverridePropertyName("Price");

        this.AddLocationRules(localizer, x => x.Latitude, x => x.Longitude);

        RuleFor(x => x.RadiusKm)
            .Must((q, radius) => radius > 0 && radius <= AdRules.MaxSearchRadiusKm && q.Latitude.HasValue && q.Longitude.HasValue)
            .When(q => q.RadiusKm.HasValue)
            .WithMessage(_ => localizer.Get(MessageKeys.RadiusInvalid, AdRules.MaxSearchRadiusKm));

        this.AddPagingRules(localizer, x => x.Page, x => x.PageSize);
    }
}

public sealed class SearchAdsQueryHandler : IQueryHandler<SearchAdsQuery, PagedResult<AdSummaryDto>>
{
    private readonly IAdRepository _ads;
    private readonly TimeProvider _time;

    public SearchAdsQueryHandler(IAdRepository ads, TimeProvider time)
    {
        _ads = ads;
        _time = time;
    }

    public async Task<PagedResult<AdSummaryDto>> HandleAsync(SearchAdsQuery query, CancellationToken cancellationToken)
    {
        var criteria = new AdSearchCriteria(
            string.IsNullOrWhiteSpace(query.Text) ? null : query.Text.Trim(),
            query.CategoryId,
            query.MinPrice,
            query.MaxPrice,
            query.Latitude,
            query.Longitude,
            query.RadiusKm,
            query.Page,
            query.PageSize);

        var (hits, totalCount) = await _ads.SearchAsync(criteria, _time.GetUtcNow().UtcDateTime, cancellationToken);

        return new PagedResult<AdSummaryDto>(
            hits.Select(hit => AdMapper.ToSummary(hit.Ad, hit.DistanceKm)).ToList(),
            query.Page,
            query.PageSize,
            totalCount);
    }
}

// ---- Categories ----

public sealed record GetCategoriesQuery : IQuery<IReadOnlyList<CategoryDto>>;

public sealed class GetCategoriesQueryHandler : IQueryHandler<GetCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    private readonly ICategoryRepository _categories;
    private readonly ILocalizer _localizer;

    public GetCategoriesQueryHandler(ICategoryRepository categories, ILocalizer localizer)
    {
        _categories = categories;
        _localizer = localizer;
    }

    public async Task<IReadOnlyList<CategoryDto>> HandleAsync(GetCategoriesQuery query, CancellationToken cancellationToken)
    {
        var categories = await _categories.GetAllAsync(cancellationToken);

        return categories
            .Select(c => new CategoryDto(c.Id, c.Key, _localizer.Get(MessageKeys.Category(c.Key))))
            .ToList();
    }
}
