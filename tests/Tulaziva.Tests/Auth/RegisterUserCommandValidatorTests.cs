using FluentAssertions;
using Tulaziva.Application.Auth;
using Tulaziva.Application.Localization;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Auth;

public class RegisterUserCommandValidatorTests
{
    private readonly RegisterUserCommandValidator _validator = new(new Localizer());

    private static RegisterUserCommand Valid() => new("ana@mail.com", "Password1", "Ana", "es", 19.4326, -99.1332, AcceptedTerms: true);

    [Fact]
    public void AValidCommand_Passes()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void WithoutAcceptingTheTerms_Fails_InTheLanguageOfTheRequest()
    {
        var result = _validator.Validate(Valid() with { AcceptedTerms = false });

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("AcceptedTerms");
    }

    [Fact]
    public void ALocationAndALanguageAreOptional()
    {
        _validator.Validate(Valid() with { Language = null, Latitude = null, Longitude = null }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public void AnInvalidEmail_Fails(string email)
    {
        var result = _validator.Validate(Valid() with { Email = email });

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Email");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Short1")]
    [InlineData("onlyletters")]
    [InlineData("12345678")]
    public void AWeakPassword_Fails(string password)
    {
        var result = _validator.Validate(Valid() with { Password = password });

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Password");
    }

    [Fact]
    public void AMissingName_Fails()
    {
        var result = _validator.Validate(Valid() with { DisplayName = " " });

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("DisplayName");
    }

    [Fact]
    public void AnUnsupportedLanguage_Fails()
    {
        var result = _validator.Validate(Valid() with { Language = "fr" });

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Language");
    }

    [Theory]
    [InlineData(90.1, 0)]
    [InlineData(-90.1, 0)]
    [InlineData(0, 180.1)]
    [InlineData(0, -180.1)]
    public void CoordinatesOutOfRange_Fail(double latitude, double longitude)
    {
        var result = _validator.Validate(Valid() with { Latitude = latitude, Longitude = longitude });

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(90, 180)]
    [InlineData(-90, -180)]
    public void CoordinatesAtTheLimits_Pass(double latitude, double longitude)
    {
        _validator.Validate(Valid() with { Latitude = latitude, Longitude = longitude }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void OnlyOneCoordinate_Fails()
    {
        var result = _validator.Validate(Valid() with { Longitude = null });

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Location");
    }

    [Theory]
    [InlineData("es", "El correo es obligatorio.")]
    [InlineData("en", "Email is required.")]
    public void TheMessagesComeInTheLanguageOfTheRequest(string language, string expected)
    {
        using var _ = new CultureScope(language);

        var result = _validator.Validate(Valid() with { Email = "" });

        result.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Be(expected);
    }
}
