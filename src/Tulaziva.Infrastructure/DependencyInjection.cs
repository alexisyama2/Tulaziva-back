using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tulaziva.Application.Abstractions;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Admin;
using Tulaziva.Application.Auth;
using Tulaziva.Application.Blocks;
using Tulaziva.Application.Users;
using Tulaziva.Application.Chat;
using Tulaziva.Application.Favorites;
using Tulaziva.Application.Reports;
using Tulaziva.Infrastructure.Email;
using Tulaziva.Infrastructure.Persistence;
using Tulaziva.Infrastructure.Security;
using Tulaziva.Infrastructure.Storage;

namespace Tulaziva.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Tulaziva";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TulazivaDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString(ConnectionStringName)));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAdRepository, AdRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IFavoriteRepository, FavoriteRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.AddScoped<IUserTokenRepository, UserTokenRepository>();
        services.AddScoped<IBlockRepository, BlockRepository>();
        services.AddScoped<IAccountDataRepository, AccountDataRepository>();
        services.AddSingleton<IFeaturingPolicy, ConfigurationFeaturingPolicy>();

        // Emails: they are written to the log until Email:Smtp:Host is set.
        services.Configure<EmailOptions>(configuration.GetSection("Email"));
        services.AddSingleton<IAppLinks, AppLinks>();

        var email = configuration.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();

        services.AddSingleton<IEmailLogWriter, EmailLogWriter>();
        services.AddScoped<IEmailLogRepository, EmailLogRepository>();
        services.AddScoped<IEmailDiagnostics, EmailDiagnostics>();

        if (email.Smtp.IsConfigured)
        {
            // Fail at startup, not at the first sign-up, if the sender of the emails is not an address.
            email.ResolveFrom();

            // The app only queues the email; a background service sends it and retries.
            services.AddSingleton(Channel.CreateBounded<QueuedEmail>(new BoundedChannelOptions(500) { SingleReader = true }));
            services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
            services.AddSingleton<IEmailSender, QueuedEmailSender>();
            services.AddHostedService<EmailDispatcherService>();
        }
        else
        {
            services.AddSingleton<IEmailSender, LogEmailSender>();
        }

        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.AddSingleton<IFileStorage>(provider =>
        {
            var root = provider.GetRequiredService<IOptions<StorageOptions>>().Value.RootPath;

            // A relative folder lives under the content root of the application, not under the folder the process started from.
            return new LocalFileStorage(Path.Combine(provider.GetRequiredService<IHostEnvironment>().ContentRootPath, root));
        });
        services.AddScoped<DatabaseInitializer>();
        services.Configure<DatabaseOptions>(configuration.GetSection("Database"));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.AddSingleton<ITokenService, JwtTokenService>();

        return services;
    }
}
