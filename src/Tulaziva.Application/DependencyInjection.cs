using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Tulaziva.Application.Auth;
using Tulaziva.Application.Chat;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;

namespace Tulaziva.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddSingleton<ILocalizer, Localizer>();
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped<IAccountEmailService, AccountEmailService>();
        services.AddScoped<ISessionService, SessionService>();

        // A host with a live channel (the API, with SignalR) registers its own after this one.
        services.AddSingleton<IRealtimeNotifier>(NullRealtimeNotifier.Instance);
        services.AddValidatorsFromAssembly(assembly);

        // Every command and query handler of this assembly, so a new use case needs no extra registration.
        var handlerTypes = new[] { typeof(ICommandHandler<,>), typeof(IQueryHandler<,>) };

        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var handlerInterface in type.GetInterfaces().Where(i => i.IsGenericType && handlerTypes.Contains(i.GetGenericTypeDefinition())))
                services.AddScoped(handlerInterface, type);
        }

        return services;
    }
}
