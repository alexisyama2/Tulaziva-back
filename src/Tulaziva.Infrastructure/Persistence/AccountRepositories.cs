using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Blocks;
using Tulaziva.Application.Users;
using Tulaziva.Domain.Entities;

namespace Tulaziva.Infrastructure.Persistence;

public sealed class BlockRepository : IBlockRepository
{
    private readonly TulazivaDbContext _db;

    public BlockRepository(TulazivaDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsEitherWayAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken) =>
        _db.UserBlocks.AnyAsync(
            b => (b.BlockerId == userId && b.BlockedId == otherUserId) || (b.BlockerId == otherUserId && b.BlockedId == userId),
            cancellationToken);

    public Task<bool> ExistsAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken) =>
        _db.UserBlocks.AnyAsync(b => b.BlockerId == blockerId && b.BlockedId == blockedId, cancellationToken);

    public async Task AddAsync(UserBlock block, CancellationToken cancellationToken) =>
        await _db.UserBlocks.AddAsync(block, cancellationToken);

    public async Task RemoveAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken)
    {
        var block = await _db.UserBlocks.FirstOrDefaultAsync(b => b.BlockerId == blockerId && b.BlockedId == blockedId, cancellationToken);

        if (block != null)
            _db.UserBlocks.Remove(block);
    }

    public async Task<IReadOnlyList<UserBlock>> GetByBlockerAsync(Guid blockerId, CancellationToken cancellationToken) =>
        await _db.UserBlocks
            .AsNoTracking()
            .Include(b => b.Blocked)
            .Where(b => b.BlockerId == blockerId && b.Blocked.IsActive)
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _db.SaveChangesAsync(cancellationToken);
}

public sealed class AccountDataRepository : IAccountDataRepository
{
    private readonly TulazivaDbContext _db;

    public AccountDataRepository(TulazivaDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<string>> RemoveOwnedDataAsync(Guid userId, CancellationToken cancellationToken)
    {
        // The favorites that other users saved of these ads go with each ad (cascade).
        var ads = await _db.Ads.Include(a => a.Images).Where(a => a.OwnerId == userId).ToListAsync(cancellationToken);
        var files = ads.SelectMany(a => a.Images).Select(i => i.StoragePath).ToList();

        _db.Ads.RemoveRange(ads);
        _db.Favorites.RemoveRange(await _db.Favorites.Where(f => f.UserId == userId).ToListAsync(cancellationToken));
        _db.UserBlocks.RemoveRange(await _db.UserBlocks.Where(b => b.BlockerId == userId || b.BlockedId == userId).ToListAsync(cancellationToken));

        return files;
    }
}

/// <summary>Reads Featuring:OwnersCanFeature. Off unless it is set, because nothing charges for featuring yet.</summary>
public sealed class ConfigurationFeaturingPolicy : IFeaturingPolicy
{
    private readonly IConfiguration _configuration;

    public ConfigurationFeaturingPolicy(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool OwnersCanFeature => _configuration.GetValue<bool>("Featuring:OwnersCanFeature");
}
