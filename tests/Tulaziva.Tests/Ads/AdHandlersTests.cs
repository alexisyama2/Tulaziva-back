using FluentAssertions;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Common;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Ads;

public class AdHandlersTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeAdRepository _ads = new();
    private readonly FakeUserRepository _users = new();
    private readonly FakeFileStorage _storage = new();
    private readonly TimeProvider _time = new FixedTimeProvider(Now);

    private readonly User _owner = new() { Email = "ana@mail.com", DisplayName = "Ana", RegistrationLatitude = 19.4326, RegistrationLongitude = -99.1332 };
    private readonly User _other = new() { Email = "luis@mail.com", DisplayName = "Luis" };

    public AdHandlersTests()
    {
        _users.Users.AddRange(new[] { _owner, _other });
    }

    private Ad NewAd(DateTime? expiresAtUtc = null, AdStatus status = AdStatus.Active, User? owner = null)
    {
        var ad = new Ad
        {
            OwnerId = (owner ?? _owner).Id,
            Owner = owner ?? _owner,
            CategoryId = 1,
            Title = "Bicicleta",
            Description = "Casi nueva",
            Price = 1500,
            Currency = "MXN",
            Latitude = 20,
            Longitude = -100,
            Status = status,
            CreatedAtUtc = Now.UtcDateTime.AddDays(-1),
            UpdatedAtUtc = Now.UtcDateTime.AddDays(-1),
            ExpiresAtUtc = expiresAtUtc ?? Now.UtcDateTime.AddDays(10)
        };

        _ads.Ads.Add(ad);
        return ad;
    }

    // ---- Create ----

    private CreateAdCommandHandler CreateHandler() => new(_ads, _users, _time);

    private CreateAdCommand CreateCommand(Guid? userId = null, double? latitude = null, double? longitude = null, string currency = "mxn") =>
        new(userId ?? _owner.Id, 1, "  Bicicleta  ", " Casi nueva ", 1500, currency, latitude, longitude);

    [Fact]
    public async Task Create_PublishesAnActiveAdThatExpiresIn30Days()
    {
        var dto = await CreateHandler().HandleAsync(CreateCommand(), CancellationToken.None);

        var ad = _ads.Ads.Should().ContainSingle().Subject;
        ad.Status.Should().Be(AdStatus.Active);
        ad.Title.Should().Be("Bicicleta");
        ad.Description.Should().Be("Casi nueva");
        ad.Currency.Should().Be("MXN");
        ad.CreatedAtUtc.Should().Be(Now.UtcDateTime);
        ad.ExpiresAtUtc.Should().Be(Now.UtcDateTime.AddDays(30));
        dto.OwnerDisplayName.Should().Be("Ana");
        dto.Status.Should().Be("Active");
        _ads.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Create_WithoutLocation_UsesTheLocationWhereTheOwnerRegistered()
    {
        await CreateHandler().HandleAsync(CreateCommand(), CancellationToken.None);

        var ad = _ads.Ads.Single();
        ad.Latitude.Should().Be(19.4326);
        ad.Longitude.Should().Be(-99.1332);
    }

    [Fact]
    public async Task Create_WithALocation_UsesThatLocation()
    {
        await CreateHandler().HandleAsync(CreateCommand(latitude: 25.6866, longitude: -100.3161), CancellationToken.None);

        var ad = _ads.Ads.Single();
        ad.Latitude.Should().Be(25.6866);
        ad.Longitude.Should().Be(-100.3161);
    }

    [Fact]
    public async Task Create_ForAnUnknownOrDisabledUser_ThrowsNotFound()
    {
        _other.IsActive = false;

        await ((Func<Task>)(() => CreateHandler().HandleAsync(CreateCommand(userId: Guid.NewGuid()), CancellationToken.None)))
            .Should().ThrowAsync<NotFoundException>();
        await ((Func<Task>)(() => CreateHandler().HandleAsync(CreateCommand(userId: _other.Id), CancellationToken.None)))
            .Should().ThrowAsync<NotFoundException>();

        _ads.Ads.Should().BeEmpty();
    }

    // ---- Update ----

    private UpdateAdCommandHandler UpdateHandler() => new(_ads, _time);

    private UpdateAdCommand UpdateCommand(Guid adId, Guid? userId = null, double? latitude = null, double? longitude = null) =>
        new(userId ?? _owner.Id, adId, 2, "Bici de montaña", "Con casco", 1200, "usd", latitude, longitude);

    [Fact]
    public async Task Update_ChangesTheFieldsOfTheOwnAd_AndKeepsTheLocationWhenNoneIsSent()
    {
        var ad = NewAd();

        var dto = await UpdateHandler().HandleAsync(UpdateCommand(ad.Id), CancellationToken.None);

        ad.CategoryId.Should().Be(2);
        ad.Title.Should().Be("Bici de montaña");
        ad.Price.Should().Be(1200);
        ad.Currency.Should().Be("USD");
        ad.UpdatedAtUtc.Should().Be(Now.UtcDateTime);
        ad.Latitude.Should().Be(20);
        ad.Longitude.Should().Be(-100);
        dto.Title.Should().Be("Bici de montaña");
    }

    [Fact]
    public async Task Update_WithALocation_MovesTheAd()
    {
        var ad = NewAd();

        await UpdateHandler().HandleAsync(UpdateCommand(ad.Id, latitude: 25, longitude: -101), CancellationToken.None);

        ad.Latitude.Should().Be(25);
        ad.Longitude.Should().Be(-101);
    }

    [Fact]
    public async Task Update_AdOfAnotherUser_ThrowsForbiddenAndChangesNothing()
    {
        var ad = NewAd();

        var act = () => UpdateHandler().HandleAsync(UpdateCommand(ad.Id, userId: _other.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ForbiddenException>();
        exception.Which.MessageKey.Should().Be(MessageKeys.Forbidden);
        ad.Title.Should().Be("Bicicleta");
        _ads.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Update_UnknownAd_ThrowsNotFound()
    {
        var act = () => UpdateHandler().HandleAsync(UpdateCommand(Guid.NewGuid()), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.MessageKey.Should().Be(MessageKeys.AdNotFound);
    }

    [Fact]
    public async Task Update_AdRemovedByAnAdmin_ThrowsConflict()
    {
        var ad = NewAd(status: AdStatus.Removed);

        var act = () => UpdateHandler().HandleAsync(UpdateCommand(ad.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ConflictException>();
        exception.Which.MessageKey.Should().Be(MessageKeys.AdRemoved);
    }

    // ---- Delete ----

    [Fact]
    public async Task Delete_RemovesTheAdAndItsFiles()
    {
        var ad = NewAd();
        _storage.Files["ads/x/1.jpg"] = TestImages.Jpeg();
        ad.Images.Add(new AdImage { AdId = ad.Id, StoragePath = "ads/x/1.jpg", ContentType = "image/jpeg" });

        await new DeleteAdCommandHandler(_ads, _storage).HandleAsync(new DeleteAdCommand(_owner.Id, ad.Id), CancellationToken.None);

        _ads.Ads.Should().BeEmpty();
        _storage.Files.Should().BeEmpty();
        _ads.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Delete_AdOfAnotherUser_ThrowsForbiddenAndKeepsEverything()
    {
        var ad = NewAd();
        _storage.Files["ads/x/1.jpg"] = TestImages.Jpeg();
        ad.Images.Add(new AdImage { AdId = ad.Id, StoragePath = "ads/x/1.jpg" });

        var act = () => new DeleteAdCommandHandler(_ads, _storage).HandleAsync(new DeleteAdCommand(_other.Id, ad.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        _ads.Ads.Should().ContainSingle();
        _storage.Files.Should().ContainSingle();
    }

    // ---- Renew ----

    [Fact]
    public async Task Renew_GivesAnExpiredAdAnother30DaysFromNow()
    {
        var ad = NewAd(expiresAtUtc: Now.UtcDateTime.AddDays(-3));

        var dto = await new RenewAdCommandHandler(_ads, _time).HandleAsync(new RenewAdCommand(_owner.Id, ad.Id), CancellationToken.None);

        ad.ExpiresAtUtc.Should().Be(Now.UtcDateTime.AddDays(30));
        dto.ExpiresAtUtc.Should().Be(Now.UtcDateTime.AddDays(30));
    }

    [Fact]
    public async Task Renew_AdOfAnotherUser_ThrowsForbidden()
    {
        var ad = NewAd();

        var act = () => new RenewAdCommandHandler(_ads, _time).HandleAsync(new RenewAdCommand(_other.Id, ad.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Renew_AdRemovedByAnAdmin_ThrowsConflict()
    {
        var ad = NewAd(status: AdStatus.Removed);

        var act = () => new RenewAdCommandHandler(_ads, _time).HandleAsync(new RenewAdCommand(_owner.Id, ad.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    // ---- Remove (admin) ----

    [Fact]
    public async Task Remove_TakesTheAdDown_AndDoingItTwiceIsHarmless()
    {
        var ad = NewAd();
        var handler = new RemoveAdCommandHandler(_ads, _time);

        await handler.HandleAsync(new RemoveAdCommand(ad.Id), CancellationToken.None);
        await handler.HandleAsync(new RemoveAdCommand(ad.Id), CancellationToken.None);

        ad.Status.Should().Be(AdStatus.Removed);
        _ads.SaveCount.Should().Be(1);
    }

    // ---- Visibility ----

    private async Task<bool> CanSee(Ad ad, Requester requester)
    {
        try
        {
            await new GetAdQueryHandler(_ads, _time).HandleAsync(new GetAdQuery(ad.Id, requester), CancellationToken.None);
            return true;
        }
        catch (NotFoundException)
        {
            return false;
        }
    }

    [Fact]
    public async Task GetAd_ActiveAd_IsPublic()
    {
        var ad = NewAd();

        (await CanSee(ad, Requester.Anonymous)).Should().BeTrue();
        (await CanSee(ad, new Requester(_other.Id))).Should().BeTrue();
    }

    [Fact]
    public async Task GetAd_ExpiredAd_IsOnlyForItsOwnerAndTheAdmins()
    {
        var ad = NewAd(expiresAtUtc: Now.UtcDateTime.AddMinutes(-1));

        (await CanSee(ad, Requester.Anonymous)).Should().BeFalse();
        (await CanSee(ad, new Requester(_other.Id))).Should().BeFalse();
        (await CanSee(ad, new Requester(_owner.Id))).Should().BeTrue();
        (await CanSee(ad, new Requester(_other.Id, IsAdmin: true))).Should().BeTrue();
    }

    [Fact]
    public async Task GetAd_RemovedAd_IsOnlyForItsOwnerAndTheAdmins()
    {
        var ad = NewAd(status: AdStatus.Removed);

        (await CanSee(ad, Requester.Anonymous)).Should().BeFalse();
        (await CanSee(ad, new Requester(_other.Id))).Should().BeFalse();
        (await CanSee(ad, new Requester(_owner.Id))).Should().BeTrue();
        (await CanSee(ad, new Requester(_other.Id, IsAdmin: true))).Should().BeTrue();
    }

    [Fact]
    public async Task GetAd_UnknownAd_ThrowsNotFound()
    {
        var act = () => new GetAdQueryHandler(_ads, _time).HandleAsync(new GetAdQuery(Guid.NewGuid(), Requester.Anonymous), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ---- Lists ----

    [Fact]
    public async Task GetMyAds_ReturnsOnlyTheAdsOfTheUser_WhateverTheirStatus()
    {
        NewAd();
        NewAd(status: AdStatus.Removed);
        NewAd(owner: _other);

        var page = await new GetMyAdsQueryHandler(_ads).HandleAsync(new GetMyAdsQuery(_owner.Id), CancellationToken.None);

        page.TotalCount.Should().Be(2);
        page.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Search_PassesTheCleanedFiltersAndMapsTheDistanceAndTheCover()
    {
        var ad = NewAd();
        var image = new AdImage { AdId = ad.Id, Position = 3 };
        ad.Images.AddRange(new[] { new AdImage { AdId = ad.Id, Position = 5 }, image });
        _ads.SearchResult.Add(new AdSearchHit(ad, 12.3456));

        var query = new SearchAdsQuery(Text: "  bici ", CategoryId: 1, MinPrice: 100, MaxPrice: 2000, Latitude: 19.4, Longitude: -99.1, RadiusKm: 50, Page: 2, PageSize: 10);
        var page = await new SearchAdsQueryHandler(_ads, _time).HandleAsync(query, CancellationToken.None);

        _ads.LastCriteria.Should().Be(new AdSearchCriteria("bici", 1, 100, 2000, 19.4, -99.1, 50, 2, 10));
        var summary = page.Items.Should().ContainSingle().Subject;
        summary.DistanceKm.Should().Be(12.3);
        summary.CoverImageUrl.Should().Be(AdRoutes.Image(ad.Id, image.Id));
        page.Page.Should().Be(2);
        page.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task Search_WithBlankText_SearchesWithoutText()
    {
        await new SearchAdsQueryHandler(_ads, _time).HandleAsync(new SearchAdsQuery(Text: "   "), CancellationToken.None);

        _ads.LastCriteria!.Text.Should().BeNull();
    }

    // ---- Categories ----

    [Theory]
    [InlineData("es", "Vehículos", "Servicios personales")]
    [InlineData("en", "Vehicles", "Personal services")]
    public async Task GetCategories_ReturnsTheNamesInTheLanguageOfTheRequest(string language, string first, string last)
    {
        using var _ = new CultureScope(language);

        var categories = await new GetCategoriesQueryHandler(new FakeCategoryRepository(), new Localizer())
            .HandleAsync(new GetCategoriesQuery(), CancellationToken.None);

        categories.Should().HaveCount(DefaultCategories.All.Count);
        categories.First().Name.Should().Be(first);
        categories.Last().Name.Should().Be(last);
    }
}
