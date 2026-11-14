using FluentAssertions;
using FluentValidation;
using Tulaziva.Application.Common;
using Tulaziva.Application.Localization;
using Tulaziva.Application.Users;
using Tulaziva.Domain.Entities;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Users;

public class ProfileTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeUserTokenRepository _tokens = new();
    private readonly User _ana = new()
    {
        Email = "ana@mail.com",
        DisplayName = "Ana",
        PreferredLanguage = "es",
        PasswordHash = "hashed:OldPass1",
        RegistrationLatitude = 19.4,
        RegistrationLongitude = -99.1
    };

    public ProfileTests()
    {
        _users.Users.Add(_ana);
    }

    private Task<UserDto> Update(string name, string? language = null, double? latitude = null, double? longitude = null) =>
        new UpdateProfileCommandHandler(_users).HandleAsync(new UpdateProfileCommand(_ana.Id, name, language, latitude, longitude), CancellationToken.None);

    // ---- Edit profile ----

    [Fact]
    public async Task Update_ChangesTheNameTheLanguageAndTheLocation()
    {
        var dto = await Update("  Ana Maria ", "en", 40.4, -3.7);

        dto.DisplayName.Should().Be("Ana Maria");
        dto.PreferredLanguage.Should().Be("en");
        dto.Latitude.Should().Be(40.4);
        dto.Longitude.Should().Be(-3.7);
        _users.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Update_WithoutLanguageNorLocation_KeepsThem()
    {
        var dto = await Update("Ana Maria");

        dto.PreferredLanguage.Should().Be("es");
        dto.Latitude.Should().Be(19.4);
        dto.Longitude.Should().Be(-99.1);
    }

    [Fact]
    public async Task Update_UnknownOrDisabledUser_ThrowsNotFound()
    {
        _ana.IsActive = false;

        var act = () => Update("Ana");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData("Ana", null, null, null, true)]
    [InlineData("", null, null, null, false)]
    [InlineData("   ", null, null, null, false)]
    [InlineData("Ana", "fr", null, null, false)]
    [InlineData("Ana", "en", null, null, true)]
    [InlineData("Ana", null, 10.0, null, false)]
    [InlineData("Ana", null, 91.0, 10.0, false)]
    [InlineData("Ana", null, 10.0, 20.0, true)]
    public void UpdateValidator_AppliesTheRulesOfTheSignUp(string name, string? language, double? latitude, double? longitude, bool valid)
    {
        var result = new UpdateProfileCommandValidator(new Localizer()).Validate(new UpdateProfileCommand(Guid.NewGuid(), name, language, latitude, longitude));

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public void UpdateValidator_RefusesALongName()
    {
        var result = new UpdateProfileCommandValidator(new Localizer()).Validate(new UpdateProfileCommand(Guid.NewGuid(), new string('a', 101)));

        result.IsValid.Should().BeFalse();
    }

    // ---- Change password ----

    private Task ChangePassword(string current, string next) =>
        new ChangePasswordCommandHandler(_users, new FakePasswordHasher(), new Localizer(), _tokens, TimeProvider.System).HandleAsync(new ChangePasswordCommand(_ana.Id, current, next), CancellationToken.None);

    [Fact]
    public async Task ChangePassword_WithTheRightCurrentOne_StoresTheNewHash()
    {
        await ChangePassword("OldPass1", "NewPass123");

        _ana.PasswordHash.Should().Be("hashed:NewPass123");
        _users.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task ChangePassword_WithAWrongCurrentOne_IsAValidationErrorNotAnAuthenticationOne()
    {
        var act = () => ChangePassword("WrongPass1", "NewPass123");

        var error = (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Single();
        error.PropertyName.Should().Be("CurrentPassword");
        _ana.PasswordHash.Should().Be("hashed:OldPass1");
        _users.SaveCount.Should().Be(0);
    }

    [Theory]
    [InlineData("", "NewPass123", false)]
    [InlineData("OldPass1", "short1", false)]
    [InlineData("OldPass1", "nodigitshere", false)]
    [InlineData("OldPass1", "NewPass123", true)]
    public void ChangePasswordValidator_RequiresTheCurrentPasswordAndAGoodNewOne(string current, string next, bool valid)
    {
        new ChangePasswordCommandValidator(new Localizer()).Validate(new ChangePasswordCommand(Guid.NewGuid(), current, next)).IsValid.Should().Be(valid);
    }
}
