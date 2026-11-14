using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Tulaziva.Application;
using Tulaziva.Application.Abstractions;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Auth;
using Tulaziva.Application.Blocks;
using Tulaziva.Application.Chat;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Favorites;
using Tulaziva.Application.Reports;
using Tulaziva.Application.Users;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Cqrs;

public class DispatcherTests
{
    private readonly FakeUserRepository _users = new();
    private readonly ServiceProvider _provider;

    public DispatcherTests()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IUserRepository>(_users);
        services.AddSingleton<IPasswordHasher, FakePasswordHasher>();
        services.AddSingleton<ITokenService, FakeTokenService>();
        services.AddSingleton<IAdRepository, FakeAdRepository>();
        services.AddSingleton<ICategoryRepository, FakeCategoryRepository>();
        services.AddSingleton<IFileStorage, FakeFileStorage>();
        services.AddSingleton<IConversationRepository, FakeConversationRepository>();
        services.AddSingleton<IFavoriteRepository, FakeFavoriteRepository>();
        services.AddSingleton<IReportRepository>(sp => new FakeReportRepository((FakeAdRepository)sp.GetRequiredService<IAdRepository>(), _users));
        services.AddSingleton<IUserTokenRepository, FakeUserTokenRepository>();
        services.AddSingleton<IEmailSender, CapturingEmailSender>();
        services.AddSingleton<IAppLinks, StaticAppLinks>();
        services.AddSingleton<IBlockRepository, FakeBlockRepository>();
        services.AddSingleton<IAccountDataRepository, FakeAccountDataRepository>();
        services.AddSingleton<IFeaturingPolicy, FakeFeaturingPolicy>();
        services.AddSingleton<Tulaziva.Application.Admin.IEmailLogRepository, FakeEmailLogRepository>();
        services.AddSingleton<Tulaziva.Application.Admin.IEmailDiagnostics, FakeEmailDiagnostics>();
        services.AddSingleton(TimeProvider.System);
        _services = services;
        _provider = services.BuildServiceProvider();
    }

    private readonly IServiceCollection _services;

    [Fact]
    public void EveryHandlerAndValidatorCanBeResolved_SoNoDependencyIsMissingInTheWiring()
    {
        using var scope = _provider.CreateScope();

        var handlers = _services
            .Where(d => d.ServiceType.IsGenericType &&
                        (d.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) ||
                         d.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
            .ToList();

        handlers.Should().HaveCountGreaterThan(10);

        foreach (var handler in handlers)
            scope.ServiceProvider.GetRequiredService(handler.ServiceType).Should().NotBeNull(handler.ServiceType.ToString());

        foreach (var validator in _services.Where(d => d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(IValidator<>)))
            scope.ServiceProvider.GetRequiredService(validator.ServiceType).Should().NotBeNull(validator.ServiceType.ToString());
    }

    [Fact]
    public async Task Send_ACommandWithoutResult_ReturnsUnit()
    {
        var ad = new Tulaziva.Domain.Entities.Ad { OwnerId = Guid.NewGuid(), ExpiresAtUtc = DateTime.UtcNow.AddDays(1) };
        await _provider.GetRequiredService<IAdRepository>().AddAsync(ad, CancellationToken.None);

        var result = await Dispatcher.SendAsync(new Tulaziva.Application.Ads.RemoveAdCommand(ad.Id));

        result.Should().Be(default(Unit));
        ad.Status.Should().Be(Tulaziva.Domain.Enums.AdStatus.Removed);
    }

    private IDispatcher Dispatcher => _provider.CreateScope().ServiceProvider.GetRequiredService<IDispatcher>();

    [Fact]
    public async Task Send_RunsTheHandlerOfTheCommand()
    {
        var response = await Dispatcher.SendAsync(new RegisterUserCommand("ana@mail.com", "Password1", "Ana", AcceptedTerms: true));

        response.User.Email.Should().Be("ana@mail.com");
        _users.Users.Should().ContainSingle();
    }

    [Fact]
    public async Task Send_InvalidCommand_ThrowsAllTheFailuresAndNeverReachesTheHandler()
    {
        using var _ = new CultureScope("en");

        var act = () => Dispatcher.SendAsync(new RegisterUserCommand("not-an-email", "short", ""));

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Select(e => e.PropertyName).Should().Contain(new[] { "Email", "Password", "DisplayName" });
        _users.Users.Should().BeEmpty();
        _users.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Send_PassesTheFailuresOfTheHandlerThrough()
    {
        await Dispatcher.SendAsync(new RegisterUserCommand("ana@mail.com", "Password1", "Ana", AcceptedTerms: true));

        var act = () => Dispatcher.SendAsync(new RegisterUserCommand("ana@mail.com", "Password1", "Ana", AcceptedTerms: true));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Query_RunsTheHandlerOfTheQuery()
    {
        await Dispatcher.SendAsync(new RegisterUserCommand("ana@mail.com", "Password1", "Ana", AcceptedTerms: true));
        var id = _users.Users.Single().Id;

        var user = await Dispatcher.QueryAsync(new GetCurrentUserQuery(id));

        user.Email.Should().Be("ana@mail.com");
    }

    [Fact]
    public void AddApplication_RegistersEveryHandler()
    {
        using var scope = _provider.CreateScope();

        scope.ServiceProvider.GetService<ICommandHandler<LoginCommand, AuthResponse>>().Should().NotBeNull();
        scope.ServiceProvider.GetService<ICommandHandler<RegisterUserCommand, AuthResponse>>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IQueryHandler<GetCurrentUserQuery, UserDto>>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IValidator<LoginCommand>>().Should().NotBeNull();
    }
}
