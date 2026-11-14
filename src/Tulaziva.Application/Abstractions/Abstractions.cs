using Tulaziva.Domain.Entities;

namespace Tulaziva.Application.Abstractions;

public interface IUserRepository
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Users ordered by registration date, newest first. Page is 1-based.</summary>
    Task<(IReadOnlyList<User> Items, int TotalCount)> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task AddAsync(User user, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);
}

public sealed record TokenResult(string Token, DateTime ExpiresAtUtc);

public interface ITokenService
{
    TokenResult Create(User user);
}
