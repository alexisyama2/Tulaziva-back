using System.Globalization;
using Tulaziva.Application.Abstractions;
using Tulaziva.Domain.Entities;

namespace Tulaziva.Tests.Fakes;

public sealed class FakeUserRepository : IUserRepository
{
    public List<User> Users { get; } = new();

    public int SaveCount { get; private set; }

    /// <summary>Makes SaveChangesAsync fail, to check what the handlers clean up.</summary>
    public bool FailOnSave { get; set; }

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(Users.Any(u => u.Email == email));

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Email == email));

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

    public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        IReadOnlyList<User> items = Users
            .OrderByDescending(u => u.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult((items, Users.Count));
    }

    public Task AddAsync(User user, CancellationToken cancellationToken)
    {
        Users.Add(user);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (FailOnSave)
            throw new InvalidOperationException("The database is down.");

        SaveCount++;
        return Task.CompletedTask;
    }
}

/// <summary>Not a real hash: it keeps the handler tests fast and lets them see what was hashed.</summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => "hashed:" + password;

    public bool Verify(string password, string passwordHash) => passwordHash == "hashed:" + password;
}

public sealed class FakeTokenService : ITokenService
{
    public static readonly DateTime Expiry = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public TokenResult Create(User user) => new($"token-{user.Id}", Expiry);
}

public sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>Sets the language of the current test (CurrentUICulture) and puts the previous one back when disposed.</summary>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

    public CultureScope(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
    }

    public void Dispose() => CultureInfo.CurrentUICulture = _previous;
}
