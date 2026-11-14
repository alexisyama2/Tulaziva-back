using FluentAssertions;
using Tulaziva.Application.Auth;
using Tulaziva.Application.Common;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Auth;

public class RegisterUserCommandHandlerTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeAccountEmailService _accountEmails = new();
    private readonly RegisterUserCommandHandler _handler;

    public RegisterUserCommandHandlerTests()
    {
        _handler = new RegisterUserCommandHandler(_users, new FakePasswordHasher(), TestSessions.Create(), _accountEmails);
    }

    private static RegisterUserCommand Command(string email = "ana@mail.com", string? language = null, double? latitude = 19.4326, double? longitude = -99.1332) =>
        new(email, "Password1", "Ana", language, latitude, longitude);

    [Fact]
    public async Task Register_CreatesARegularUserWithAHashedPasswordAndTheLocation()
    {
        var response = await _handler.HandleAsync(Command(email: "  Ana@Mail.com "), CancellationToken.None);

        var user = _users.Users.Should().ContainSingle().Subject;
        user.Email.Should().Be("ana@mail.com");
        user.PasswordHash.Should().Be("hashed:Password1").And.NotContain("Password1 ");
        user.Role.Should().Be(UserRole.User);
        user.RegistrationLatitude.Should().Be(19.4326);
        user.RegistrationLongitude.Should().Be(-99.1332);
        user.IsActive.Should().BeTrue();

        response.Token.Should().Be($"token-{user.Id}");
        response.User.Email.Should().Be("ana@mail.com");
        response.User.Role.Should().Be("User");
        _users.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Register_WithoutLocation_SavesTheUserWithoutCoordinates()
    {
        await _handler.HandleAsync(Command(latitude: null, longitude: null), CancellationToken.None);

        var user = _users.Users.Single();
        user.RegistrationLatitude.Should().BeNull();
        user.RegistrationLongitude.Should().BeNull();
    }

    [Theory]
    [InlineData("ANA@mail.com")]
    [InlineData(" ana@mail.com ")]
    public async Task Register_EmailAlreadyRegistered_ThrowsConflictWhateverTheCase(string email)
    {
        _users.Users.Add(new User { Email = "ana@mail.com" });

        var act = () => _handler.HandleAsync(Command(email: email), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ConflictException>();
        exception.Which.MessageKey.Should().Be(MessageKeys.EmailTaken);
        _users.Users.Should().HaveCount(1);
        _users.SaveCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null, "es")]
    [InlineData("", "es")]
    [InlineData("en", "en")]
    [InlineData("EN", "en")]
    public async Task Register_SavesTheLanguageOrTheDefaultOne(string? language, string expected)
    {
        await _handler.HandleAsync(Command(language: language), CancellationToken.None);

        _users.Users.Single().PreferredLanguage.Should().Be(expected);
    }

    [Fact]
    public async Task Register_RecordsWhenAndWhichTermsWereAccepted()
    {
        await _handler.HandleAsync(Command() with { AcceptedTerms = true }, CancellationToken.None);

        var user = _users.Users.Should().ContainSingle().Subject;
        user.TermsVersion.Should().Be(Tulaziva.Application.Users.LegalRules.TermsVersion);
        user.TermsAcceptedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }
}
