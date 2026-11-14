using Microsoft.EntityFrameworkCore;
using Tulaziva.Application.Ads;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Infrastructure.Persistence;

public sealed class AdRepository : IAdRepository
{
    private readonly TulazivaDbContext _db;

    public AdRepository(TulazivaDbContext db)
    {
        _db = db;
    }

    public Task<Ad?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Ads
            .Include(a => a.Images)
            .Include(a => a.Owner)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Ad> Items, int TotalCount)> GetByOwnerAsync(Guid ownerId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _db.Ads.AsNoTracking().Where(a => a.OwnerId == ownerId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Include(a => a.Images)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<(IReadOnlyList<AdSearchHit> Items, int TotalCount)> SearchAsync(AdSearchCriteria criteria, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var query = _db.Ads.AsNoTracking().Where(a => a.Status == AdStatus.Active && a.ExpiresAtUtc > nowUtc);

        // Lower-cased on both sides, so the match ignores the case whatever the collation of the database is.
        if (criteria.Text is { Length: > 0 } text)
        {
            var lowered = text.ToLowerInvariant();
            query = query.Where(a => a.Title.ToLower().Contains(lowered) || a.Description.ToLower().Contains(lowered));
        }

        if (criteria.CategoryId.HasValue)
            query = query.Where(a => a.CategoryId == criteria.CategoryId.Value);

        if (criteria.MinPrice.HasValue)
            query = query.Where(a => a.Price >= criteria.MinPrice.Value);

        if (criteria.MaxPrice.HasValue)
            query = query.Where(a => a.Price <= criteria.MaxPrice.Value);

        if (criteria.Latitude is not { } latitude || criteria.Longitude is not { } longitude)
            return await SearchByDateAsync(query, criteria, nowUtc, cancellationToken);

        return await SearchByDistanceAsync(query, criteria, latitude, longitude, nowUtc, cancellationToken);
    }

    private static async Task<(IReadOnlyList<AdSearchHit> Items, int TotalCount)> SearchByDateAsync(
        IQueryable<Ad> query, AdSearchCriteria criteria, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);

        var ads = await query
            .Include(a => a.Images)
            .OrderByDescending(a => a.FeaturedUntilUtc != null && a.FeaturedUntilUtc > nowUtc)
            .ThenByDescending(a => a.CreatedAtUtc)
            .ThenBy(a => a.Id)
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync(cancellationToken);

        return (ads.Select(ad => new AdSearchHit(ad, null)).ToList(), totalCount);
    }

    // The database discards what is outside a rectangle around the point; the exact distance is computed on what is left.
    // This is enough while a search has a few thousand candidates; beyond that a spatial index is the next step.
    private static async Task<(IReadOnlyList<AdSearchHit> Items, int TotalCount)> SearchByDistanceAsync(
        IQueryable<Ad> query, AdSearchCriteria criteria, double latitude, double longitude, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (criteria.RadiusKm is { } radius)
        {
            var box = GeoMath.BoundingBox(latitude, longitude, radius);

            query = query.Where(a => a.Latitude >= box.MinLatitude && a.Latitude <= box.MaxLatitude);

            if (box.MinLongitude is { } minLongitude && box.MaxLongitude is { } maxLongitude)
                query = query.Where(a => a.Longitude >= minLongitude && a.Longitude <= maxLongitude);
        }

        var candidates = await query.Include(a => a.Images).ToListAsync(cancellationToken);

        var hits = candidates
            .Select(ad => new AdSearchHit(ad, ad.Latitude.HasValue && ad.Longitude.HasValue
                ? GeoMath.DistanceKm(latitude, longitude, ad.Latitude.Value, ad.Longitude.Value)
                : null))
            .Where(hit => criteria.RadiusKm is not { } radiusKm || (hit.DistanceKm.HasValue && hit.DistanceKm.Value <= radiusKm))
            .OrderBy(hit => hit.Ad.FeaturedUntilUtc > nowUtc ? 0 : 1)
            .ThenBy(hit => hit.DistanceKm.HasValue ? 0 : 1)
            .ThenBy(hit => hit.DistanceKm)
            .ThenByDescending(hit => hit.Ad.CreatedAtUtc)
            .ThenBy(hit => hit.Ad.Id)
            .ToList();

        var page = hits
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToList();

        return (page, hits.Count);
    }

    public async Task AddAsync(Ad ad, CancellationToken cancellationToken) =>
        await _db.Ads.AddAsync(ad, cancellationToken);

    public void Remove(Ad ad) => _db.Ads.Remove(ad);

    public void AddImage(AdImage image) => _db.AdImages.Add(image);

    public void RemoveImage(AdImage image) => _db.AdImages.Remove(image);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}

public sealed class CategoryRepository : ICategoryRepository
{
    private readonly TulazivaDbContext _db;

    public CategoryRepository(TulazivaDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken cancellationToken) =>
        await _db.Categories.AsNoTracking().OrderBy(c => c.Id).ToListAsync(cancellationToken);

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken) =>
        _db.Categories.AnyAsync(c => c.Id == id, cancellationToken);
}
