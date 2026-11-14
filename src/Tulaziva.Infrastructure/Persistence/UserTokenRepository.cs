using Microsoft.EntityFrameworkCore;
using Tulaziva.Application.Auth;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Infrastructure.Persistence;

public sealed class UserTokenRepository : IUserTokenRepository
{
    private readonly TulazivaDbContext _db;

    public UserTokenRepository(TulazivaDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(UserToken token, CancellationToken cancellationToken) =>
        await _db.UserTokens.AddAsync(token, cancellationToken);

    public Task<UserToken?> FindValidAsync(string tokenHash, TokenPurpose purpose, DateTime nowUtc, CancellationToken cancellationToken) =>
        _db.UserTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(
                t => t.TokenHash == tokenHash && t.Purpose == purpose && t.UsedAtUtc == null && t.ExpiresAtUtc > nowUtc,
                cancellationToken);

    public async Task InvalidateAsync(Guid userId, TokenPurpose purpose, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var open = await _db.UserTokens
            .Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in open)
            token.UsedAtUtc = nowUtc;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _db.SaveChangesAsync(cancellationToken);
}
