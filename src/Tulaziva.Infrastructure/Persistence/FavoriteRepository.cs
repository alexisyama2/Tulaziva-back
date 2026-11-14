using Microsoft.EntityFrameworkCore;
using Tulaziva.Application.Favorites;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Infrastructure.Persistence;

public sealed class FavoriteRepository : IFavoriteRepository
{
    private readonly TulazivaDbContext _db;

    public FavoriteRepository(TulazivaDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsAsync(Guid userId, Guid adId, CancellationToken cancellationToken) =>
        _db.Favorites.AnyAsync(f => f.UserId == userId && f.AdId == adId, cancellationToken);

    public async Task AddAsync(Favorite favorite, CancellationToken cancellationToken) =>
        await _db.Favorites.AddAsync(favorite, cancellationToken);

    public async Task RemoveAsync(Guid userId, Guid adId, CancellationToken cancellationToken)
    {
        var favorite = await _db.Favorites.FirstOrDefaultAsync(f => f.UserId == userId && f.AdId == adId, cancellationToken);

        if (favorite != null)
            _db.Favorites.Remove(favorite);
    }

    public async Task<IReadOnlyList<Guid>> GetAdIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        await _db.Favorites.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => f.AdId)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<Ad> Items, int TotalCount)> GetPageAsync(Guid userId, int page, int pageSize, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var query = _db.Favorites.AsNoTracking()
            .Where(f => f.UserId == userId && f.Ad.Status == AdStatus.Active && f.Ad.ExpiresAtUtc > nowUtc);

        var totalCount = await query.CountAsync(cancellationToken);

        // The page of ids first (in the order they were saved), then the ads with their images: Include does not work after a projection.
        var ids = await query
            .OrderByDescending(f => f.CreatedAtUtc)
            .ThenBy(f => f.AdId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => f.AdId)
            .ToListAsync(cancellationToken);

        var ads = await _db.Ads.AsNoTracking()
            .Include(a => a.Images)
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        return (ids.Select(id => ads[id]).ToList(), totalCount);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _db.SaveChangesAsync(cancellationToken);
}
