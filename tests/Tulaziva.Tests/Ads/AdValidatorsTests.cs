using FluentAssertions;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Localization;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Ads;

public class CreateAdCommandValidatorTests
{
    private readonly CreateAdCommandValidator _validator = new(new Localizer(), new FakeCategoryRepository());

    private static CreateAdCommand Valid() =>
        new(Guid.NewGuid(), 1, "Bicicleta", "Casi nueva", 1500m, "MXN", 19.4326, -99.1332);

    private async Task<string[]> FailuresOf(CreateAdCommand command) =>
        (await _validator.ValidateAsync(command)).Errors.Select(e => e.PropertyName).ToArray();

    [Fact]
    public async Task AValidAd_Passes()
    {
        (await _validator.ValidateAsync(Valid())).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task TheLocationIsOptional_AndTheCurrencyAcceptsAnyCase()
    {
        (await _validator.ValidateAsync(Valid() with { Latitude = null, Longitude = null, Currency = "usd" })).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AFreeAd_Passes()
    {
        (await _validator.ValidateAsync(Valid() with { Price = 0 })).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AnUnknownCategory_Fails()
    {
        (await FailuresOf(Valid() with { CategoryId = 999 })).Should().ContainSingle().Which.Should().Be("CategoryId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    public async Task ATitleThatIsMissingOrTooShort_Fails(string title)
    {
        (await FailuresOf(Valid() with { Title = title })).Should().ContainSingle().Which.Should().Be("Title");
    }

    [Fact]
    public async Task ATitleThatIsTooLong_Fails()
    {
        (await FailuresOf(Valid() with { Title = new string('a', AdRules.MaxTitleLength + 1) })).Should().ContainSingle().Which.Should().Be("Title");
    }

    [Fact]
    public async Task ADescriptionThatIsMissingOrTooLong_Fails()
    {
        (await FailuresOf(Valid() with { Description = "" })).Should().ContainSingle().Which.Should().Be("Description");
        (await FailuresOf(Valid() with { Description = new string('a', AdRules.MaxDescriptionLength + 1) })).Should().ContainSingle().Which.Should().Be("Description");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1_000_000_000)]
    public async Task APriceOutOfRange_Fails(double price)
    {
        (await FailuresOf(Valid() with { Price = (decimal)price })).Should().ContainSingle().Which.Should().Be("Price");
    }

    [Theory]
    [InlineData("")]
    [InlineData("XXX")]
    [InlineData("PESOS")]
    public async Task AnUnsupportedCurrency_Fails(string currency)
    {
        (await FailuresOf(Valid() with { Currency = currency })).Should().ContainSingle().Which.Should().Be("Currency");
    }

    [Fact]
    public async Task ACoordinateOutOfRange_OrOnlyOneOfThem_Fails()
    {
        (await FailuresOf(Valid() with { Latitude = 91 })).Should().Contain("Latitude");
        (await FailuresOf(Valid() with { Longitude = -181 })).Should().Contain("Longitude");
        (await FailuresOf(Valid() with { Longitude = null })).Should().ContainSingle().Which.Should().Be("Location");
    }

    [Theory]
    [InlineData("es", "La categoría no existe.")]
    [InlineData("en", "The category does not exist.")]
    public async Task TheMessagesComeInTheLanguageOfTheRequest(string language, string expected)
    {
        using var _ = new CultureScope(language);

        var result = await _validator.ValidateAsync(Valid() with { CategoryId = 999 });

        result.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Be(expected);
    }
}

public class SearchAdsQueryValidatorTests
{
    private readonly SearchAdsQueryValidator _validator = new(new Localizer());

    [Fact]
    public void ASearchWithoutFilters_Passes()
    {
        _validator.Validate(new SearchAdsQuery()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ASearchWithEveryFilter_Passes()
    {
        var query = new SearchAdsQuery("bici", 1, 100, 2000, 19.4, -99.1, 50, 2, 10);

        _validator.Validate(query).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(200.0, 100.0)]
    [InlineData(-1.0, null)]
    [InlineData(null, -1.0)]
    public void APriceRangeThatMakesNoSense_Fails(double? min, double? max)
    {
        var query = new SearchAdsQuery(MinPrice: (decimal?)min, MaxPrice: (decimal?)max);

        _validator.Validate(query).Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Price");
    }

    [Fact]
    public void ATextThatIsTooLong_Fails()
    {
        var query = new SearchAdsQuery(Text: new string('a', AdRules.MaxSearchTextLength + 1));

        _validator.Validate(query).Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Text");
    }

    [Fact]
    public void ARadiusWithoutALocation_Fails()
    {
        _validator.Validate(new SearchAdsQuery(RadiusKm: 10)).Errors.Should().ContainSingle().Which.PropertyName.Should().Be("RadiusKm");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(500.1)]
    public void ARadiusOutOfRange_Fails(double radius)
    {
        var query = new SearchAdsQuery(Latitude: 19.4, Longitude: -99.1, RadiusKm: radius);

        _validator.Validate(query).Errors.Should().ContainSingle().Which.PropertyName.Should().Be("RadiusKm");
    }

    [Fact]
    public void ALocationWithoutRadius_Passes_AndOnlyOneCoordinateFails()
    {
        _validator.Validate(new SearchAdsQuery(Latitude: 19.4, Longitude: -99.1)).IsValid.Should().BeTrue();
        _validator.Validate(new SearchAdsQuery(Latitude: 19.4)).Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Location");
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void APagingOutOfRange_Fails(int page, int pageSize)
    {
        _validator.Validate(new SearchAdsQuery(Page: page, PageSize: pageSize)).IsValid.Should().BeFalse();
    }
}
