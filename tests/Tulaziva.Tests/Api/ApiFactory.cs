using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Tulaziva.Application.Auth;
using Tulaziva.Domain.Entities;
using Tulaziva.Tests.Fakes;
using Tulaziva.Infrastructure.Persistence;

namespace Tulaziva.Tests.Api;

/// <summary>
/// Starts the whole API (routes, Swagger, authentication, middleware) with an in-memory database and a temporary
/// storage folder, so the tests need neither SQL Server nor leave files behind.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@tulaziva.local";
    public const string AdminPassword = "Admin12345"; // the development seed of appsettings.Development.json

    private readonly string _databaseName = Guid.NewGuid().ToString();

    /// <summary>Every email the API sent, so the tests can follow the links.</summary>
    public CapturingEmailSender Emails { get; } = new();

    public string StoragePath { get; } = Path.Combine(Path.GetTempPath(), "tulaziva-api-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:MigrateOnStartup"] = "false",
            // The tests must never reach a real mail server, whatever the user secrets of this machine say.
            ["Email:Smtp:Host"] = "",
            ["Email:Smtp:User"] = "",
            ["Email:Smtp:Password"] = "",
            ["Storage:RootPath"] = StoragePath
        }));

        builder.ConfigureServices(services =>
        {
            var sqlServerOptions = services.Where(d => d.ServiceType == typeof(DbContextOptions<TulazivaDbContext>)).ToList();
            foreach (var descriptor in sqlServerOptions)
                services.Remove(descriptor);

            services.AddDbContext<TulazivaDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // The API already started and seeded the admin, so the in-memory store exists; the categories (seed data of the model) are added here.
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TulazivaDbContext>();

        if (!db.Categories.Any())
        {
            db.Categories.AddRange(DefaultCategories.All.Select(c => new Category { Id = c.Id, Key = c.Key }));
            db.SaveChanges();
        }

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(StoragePath))
            Directory.Delete(StoragePath, recursive: true);
    }
}
