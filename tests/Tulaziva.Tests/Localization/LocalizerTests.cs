using FluentAssertions;
using Tulaziva.Application.Localization;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Localization;

public class LocalizerTests
{
    private readonly Localizer _localizer = new();

    [Theory]
    [InlineData("es", "Correo o contraseña incorrectos.")]
    [InlineData("en", "Incorrect email or password.")]
    [InlineData("es-MX", "Correo o contraseña incorrectos.")]
    [InlineData("en-US", "Incorrect email or password.")]
    public void Get_AnswersInTheLanguageOfTheRequest(string culture, string expected)
    {
        using var _ = new CultureScope(culture);

        _localizer.Get(MessageKeys.InvalidCredentials).Should().Be(expected);
    }

    [Fact]
    public void Get_UsesTheDefaultLanguageWhenTheCultureIsNotSupported()
    {
        using var _ = new CultureScope("fr");

        _localizer.Get(MessageKeys.InvalidCredentials).Should().Be("Correo o contraseña incorrectos.");
    }

    [Fact]
    public void Get_FillsTheArguments()
    {
        using var _ = new CultureScope("en");

        _localizer.Get(MessageKeys.PasswordTooShort, 8).Should().Be("Password must be at least 8 characters long.");
    }

    [Fact]
    public void Get_ReturnsTheKeyWhenThereIsNoTranslation()
    {
        _localizer.Get("Does.Not.Exist").Should().Be("Does.Not.Exist");
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void EveryKeyHasATranslationInEverySupportedLanguage(string language)
    {
        using var _ = new CultureScope(language);

        var keys = typeof(MessageKeys).GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!);

        foreach (var key in keys)
            _localizer.Get(key).Should().NotBe(key, $"'{key}' is missing in '{language}'");
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void EveryDefaultCategoryHasATranslationInEverySupportedLanguage(string language)
    {
        using var _ = new CultureScope(language);

        foreach (var category in Tulaziva.Domain.Entities.DefaultCategories.All)
            _localizer.Get(MessageKeys.Category(category.Key)).Should().NotBe(MessageKeys.Category(category.Key), $"'{category.Key}' is missing in '{language}'");
    }

    [Theory]
    [InlineData("es", true)]
    [InlineData("EN", true)]
    [InlineData("fr", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupported_ChecksTheList(string? code, bool expected)
    {
        SupportedLanguages.IsSupported(code).Should().Be(expected);
    }
}
