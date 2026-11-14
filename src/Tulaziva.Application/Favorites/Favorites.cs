using FluentValidation;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Entities;

namespace Tulaziva.Application.Favorites;

public interface IFavoriteRepository
{
    Task<bool> ExistsAsync(Guid userId, Guid adId, CancellationToken cancellationToken);

    Task AddAsync(Favorite favorite, CancellationToken cancellationToken);

    /// <summary>Removes the favorite if it exists. Saved with SaveChangesAsync.</summary>
    Task RemoveAsync(Guid userId, Guid adId, CancellationToken cancellationToken);

    /// <summary>The ids of all the ads the user saved (the app uses them to paint the hearts).</summary>
    Task<IReadOnlyList<Guid>> GetAdIdsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// The saved ads that are still public (active and not expired), the last saved first, with their images. An ad that expired or was
    /// taken down stays saved but is not listed. Page is 1-based.
    /// </summary>
    Task<(IReadOnlyList<Ad> Items, int TotalCount)> GetPageAsync(Guid userId, int page, int pageSize, DateTime nowUtc, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

// ---- Save ----

/// <summary>Saves a public ad. Saving it twice is harmless.</summary>
public sealed record AddFavoriteCommand(Guid UserId, Guid AdId) : ICommand<Unit>;

public sealed class AddFavoriteCommandHandler : ICommandHandler<AddFavoriteCommand, Unit>
{
    private readonly IFavoriteRepository _favorites;
    private readonly IAdRepository _ads;
    private readonly TimeProvider _time;

    public AddFavoriteCommandHandler(IFavoriteRepository favorites, IAdRepository ads, TimeProvider time)
    {
        _favorites = favorites;
        _ads = ads;
        _time = time;
    }

    public async Task<Unit> HandleAsync(AddFavoriteCommand command, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var ad = await AdAccess.GetAsync(_ads, command.AdId, cancellationToken);

        // Only what the public can see can be saved: an ad nobody may see answers "not found".
        if (!AdAccess.CanView(ad, Requester.Anonymous, now))
            throw new NotFoundException(MessageKeys.AdNotFound);

        if (await _favorites.ExistsAsync(command.UserId, ad.Id, cancellationToken))
            return default;

        await _favorites.AddAsync(new Favorite { UserId = command.UserId, AdId = ad.Id, CreatedAtUtc = now }, cancellationToken);
        await _favorites.SaveChangesAsync(cancellationToken);

        return default;
    }
}

// ---- Unsave ----

/// <summary>Removes an ad from the favorites. Doing it for an ad that was not saved is harmless.</summary>
public sealed record RemoveFavoriteCommand(Guid UserId, Guid AdId) : ICommand<Unit>;

public sealed class RemoveFavoriteCommandHandler : ICommandHandler<RemoveFavoriteCommand, Unit>
{
    private readonly IFavoriteRepository _favorites;

    public RemoveFavoriteCommandHandler(IFavoriteRepository favorites)
    {
        _favorites = favorites;
    }

    public async Task<Unit> HandleAsync(RemoveFavoriteCommand command, CancellationToken cancellationToken)
    {
        await _favorites.RemoveAsync(command.UserId, command.AdId, cancellationToken);
        await _favorites.SaveChangesAsync(cancellationToken);

        return default;
    }
}

// ---- List ----

public sealed record GetFavoritesQuery(Guid UserId, int Page = 1, int PageSize = 20) : IQuery<PagedResult<AdSummaryDto>>;

public sealed class GetFavoritesQueryValidator : AbstractValidator<GetFavoritesQuery>
{
    public GetFavoritesQueryValidator(ILocalizer localizer)
    {
        this.AddPagingRules(localizer, x => x.Page, x => x.PageSize);
    }
}

public sealed class GetFavoritesQueryHandler : IQueryHandler<GetFavoritesQuery, PagedResult<AdSummaryDto>>
{
    private readonly IFavoriteRepository _favorites;
    private readonly TimeProvider _time;

    public GetFavoritesQueryHandler(IFavoriteRepository favorites, TimeProvider time)
    {
        _favorites = favorites;
        _time = time;
    }

    public async Task<PagedResult<AdSummaryDto>> HandleAsync(GetFavoritesQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _favorites.GetPageAsync(query.UserId, query.Page, query.PageSize, _time.GetUtcNow().UtcDateTime, cancellationToken);

        return new PagedResult<AdSummaryDto>(items.Select(ad => AdMapper.ToSummary(ad)).ToList(), query.Page, query.PageSize, totalCount);
    }
}

public sealed record GetFavoriteIdsQuery(Guid UserId) : IQuery<IReadOnlyList<Guid>>;

public sealed class GetFavoriteIdsQueryHandler : IQueryHandler<GetFavoriteIdsQuery, IReadOnlyList<Guid>>
{
    private readonly IFavoriteRepository _favorites;

    public GetFavoriteIdsQueryHandler(IFavoriteRepository favorites)
    {
        _favorites = favorites;
    }

    public Task<IReadOnlyList<Guid>> HandleAsync(GetFavoriteIdsQuery query, CancellationToken cancellationToken) =>
        _favorites.GetAdIdsAsync(query.UserId, cancellationToken);
}
