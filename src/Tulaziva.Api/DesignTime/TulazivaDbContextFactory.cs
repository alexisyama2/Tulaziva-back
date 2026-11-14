using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tulaziva.Infrastructure;
using Tulaziva.Infrastructure.Persistence;

namespace Tulaziva.Api.DesignTime;

/// <summary>
/// Used only by "dotnet ef". It builds the context from appsettings, without starting the API, so the
/// migrations can be created without needing a signing key, the seed or a reachable database.
/// </summary>
public sealed class TulazivaDbContextFactory : IDesignTimeDbContextFactory<TulazivaDbContext>
{
    public TulazivaDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new DbContextOptionsBuilder<TulazivaDbContext>()
            .UseSqlServer(configuration.GetConnectionString(DependencyInjection.ConnectionStringName))
            .Options;

        return new TulazivaDbContext(options);
    }
}
