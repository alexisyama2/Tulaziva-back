using FluentAssertions;
using Tulaziva.Application.Auth;
using Tulaziva.Application.Common;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Auth;

public class LoginCommandHandlerTests
{
    private readonly FakeUserRepository _users = new();
    private readonly LoginCommandHandler _handler;
    private readonly User _ana = new() { Email = "ana@mail.com", PasswordHash = "hashed:Password1", DisplayName = "Ana", Role = UserRole.Admin };

    public LoginCommandHandlerTests()
    {
        _users.Users.Add(_ana);
        _handler = new LoginCommandHandler(_users, new FakePasswordHasher(), TestSessions.Create());
    }

    [Fact]
    public async Task Login_WithTheRightCredentials_ReturnsTheTokenAndMarksTheLogin()
    {
        var response = await _handler.HandleAsync(new LoginCommand("ana@mail.com", "Password1"), CancellationToken.None);

        response.Token.Should().Be($"token-{_ana.Id}");
        response.ExpiresAtUtc.Should().Be(FakeTokenService.Expiry);
        response.User.Role.Should().Be("Admin");
        _ana.LastLoginAtUtc.Should().NotBeNull();
        _users.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Login_IgnoresTheCaseAndTheSpacesOfTheEmail()
    {
        var response = await _handler.HandleAsync(new LoginCommand("  ANA@Mail.com ", "Password1"), CancellationToken.None);

        response.User.Id.Should().Be(_ana.Id);
    }

    [Theory]
    [InlineData("nobody@mail.com", "Password1")]
    [InlineData("ana@mail.com", "WrongPassword1")]
    public async Task Login_UnknownEmailOrWrongPassword_ThrowsTheSameError(string email, string password)
    {
        var act = () => _handler.HandleAsync(new LoginCommand(email, password), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<UnauthenticatedException>();
        exception.Which.MessageKey.Should().Be(MessageKeys.InvalidCredentials);
        _ana.LastLoginAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Login_DisabledAccount_ThrowsTheSameErrorAsWrongCredentials()
    {
        _ana.IsActive = false;

        var act = () => _handler.HandleAsync(new LoginCommand("ana@mail.com", "Password1"), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<UnauthenticatedException>();
        exception.Which.MessageKey.Should().Be(MessageKeys.InvalidCredentials);
    }
}
