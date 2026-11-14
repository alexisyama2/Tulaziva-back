using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tulaziva.Application.Abstractions;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    /// <summary>Applies the pending migrations when the API starts. Meant for development; in production run the migrations from the pipeline.</summary>
    public bool MigrateOnStartup { get; set; }

    /// <summary>The first admin, created at startup when it does not exist. Without it nobody could create admins.</summary>
    public SeedAdminOptions SeedAdmin { get; set; } = new();
}

public sealed class SeedAdminOptions
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Administrator";
}

public sealed class DatabaseInitializer
{
    private readonly TulazivaDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly DatabaseOptions _options;

    public DatabaseInitializer(TulazivaDbContext db, IPasswordHasher passwordHasher, IOptions<DatabaseOptions> options)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _options = options.Value;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_options.MigrateOnStartup)
            await _db.Database.MigrateAsync(cancellationToken);

        await SeedAdminAsync(cancellationToken);
    }

    private async Task SeedAdminAsync(CancellationToken cancellationToken)
    {
        var seed = _options.SeedAdmin;

        if (string.IsNullOrWhiteSpace(seed.Email) || string.IsNullOrWhiteSpace(seed.Password))
            return;

        var email = seed.Email.Trim().ToLowerInvariant();

        var existing = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (existing != null)
        {
            // The seeded admin was created before emails were verified; it never gets a link, so it counts as verified.
            if (existing.EmailVerifiedAtUtc == null)
            {
                existing.EmailVerifiedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        _db.Users.Add(new User
        {
            Email = email,
            PasswordHash = _passwordHasher.Hash(seed.Password),
            DisplayName = seed.DisplayName,
            Role = UserRole.Admin,
            EmailVerifiedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
