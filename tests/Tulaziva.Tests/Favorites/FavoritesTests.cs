using FluentAssertions;
using Tulaziva.Application.Common;
using Tulaziva.Application.Favorites;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Favorites;

public class FavoritesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeFavoriteRepository _favorites = new();
    private readonly FakeAdRepository _ads = new();
    private readonly MutableTimeProvider _time = new(Now);
    private readonly User _owner = new() { Email = "owner@mail.com", DisplayName = "Owner" };
    private readonly User _luis = new() { Email = "luis@mail.com", DisplayName = "Luis" };

    private Ad NewAd(AdStatus status = AdStatus.Active, int expiresInDays = 10, string title = "Bicicleta")
    {
        var ad = new Ad
        {
            OwnerId = _owner.Id,
            Owner = _owner,
            CategoryId = 1,
            Title = title,
            Currency = "MXN",
            Status = status,
            ExpiresAtUtc = Now.UtcDateTime.AddDays(expiresInDays)
        };
        _ads.Ads.Add(ad);

        return ad;
    }

    private AddFavoriteCommandHandler AddHandler() => new(_favorites, _ads, _time);

    [Fact]
    public async Task Add_SavesAPublicAd()
    {
        var ad = NewAd();

        await AddHandler().HandleAsync(new AddFavoriteCommand(_luis.Id, ad.Id), CancellationToken.None);

        var favorite = _favorites.Favorites.Should().ContainSingle().Subject;
        favorite.UserId.Should().Be(_luis.Id);
        favorite.AdId.Should().Be(ad.Id);
        favorite.CreatedAtUtc.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task Add_TheSameAdTwice_IsHarmless()
    {
        var ad = NewAd();
        var handler = AddHandler();

        await handler.HandleAsync(new AddFavoriteCommand(_luis.Id, ad.Id), CancellationToken.None);
        await handler.HandleAsync(new AddFavoriteCommand(_luis.Id, ad.Id), CancellationToken.None);

        _favorites.Favorites.Should().ContainSingle();
        _favorites.SaveCount.Should().Be(1);
    }

    [Theory]
    [InlineData(AdStatus.Removed, 10)]
    [InlineData(AdStatus.Active, -1)]
    public async Task Add_AnAdTheStreetCannotSee_ThrowsNotFound(AdStatus status, int expiresInDays)
    {
        var ad = NewAd(status, expiresInDays);

        var act = () => AddHandler().HandleAsync(new AddFavoriteCommand(_luis.Id, ad.Id), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        _favorites.Favorites.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_AnAdThatDoesNotExist_ThrowsNotFound()
    {
        var act = () => AddHandler().HandleAsync(new AddFavoriteCommand(_luis.Id, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Remove_DeletesTheFavorite_AndDoingItAgainIsHarmless()
    {
        var ad = NewAd();
        await AddHandler().HandleAsync(new AddFavoriteCommand(_luis.Id, ad.Id), CancellationToken.None);
        var handler = new RemoveFavoriteCommandHandler(_favorites);

        await handler.HandleAsync(new RemoveFavoriteCommand(_luis.Id, ad.Id), CancellationToken.None);
        var again = () => handler.HandleAsync(new RemoveFavoriteCommand(_luis.Id, ad.Id), CancellationToken.None);

        _favorites.Favorites.Should().BeEmpty();
        await again.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Remove_OnlyAffectsTheUserWhoAsked()
    {
        var ad = NewAd();
        var other = new User { Email = "other@mail.com" };
        _favorites.Favorites.Add(new Favorite { UserId = _luis.Id, AdId = ad.Id });
        _favorites.Favorites.Add(new Favorite { UserId = other.Id, AdId = ad.Id });

        await new RemoveFavoriteCommandHandler(_favorites).HandleAsync(new RemoveFavoriteCommand(_luis.Id, ad.Id), CancellationToken.None);

        _favorites.Favorites.Should().ContainSingle().Which.UserId.Should().Be(other.Id);
    }

    [Fact]
    public async Task List_ReturnsTheStillPublicAdsLastSavedFirst()
    {
        var first = NewAd(title: "First");
        var second = NewAd(title: "Second");
        var expired = NewAd(expiresInDays: -2, title: "Expired");
        var removed = NewAd(AdStatus.Removed, title: "Removed");

        foreach (var (ad, days) in new[] { (first, 3), (second, 2), (expired, 1), (removed, 0) })
            _favorites.Favorites.Add(new Favorite { UserId = _luis.Id, AdId = ad.Id, Ad = ad, CreatedAtUtc = Now.UtcDateTime.AddDays(-days) });

        var page = await new GetFavoritesQueryHandler(_favorites, _time).HandleAsync(new GetFavoritesQuery(_luis.Id), CancellationToken.None);

        page.TotalCount.Should().Be(2);
        page.Items.Select(a => a.Title).Should().Equal("Second", "First");
    }

    [Fact]
    public async Task Ids_ReturnsEverythingTheUserSaved()
    {
        var ad1 = NewAd();
        var ad2 = NewAd();
        _favorites.Favorites.Add(new Favorite { UserId = _luis.Id, AdId = ad1.Id, CreatedAtUtc = Now.UtcDateTime.AddDays(-1) });
        _favorites.Favorites.Add(new Favorite { UserId = _luis.Id, AdId = ad2.Id, CreatedAtUtc = Now.UtcDateTime });
        _favorites.Favorites.Add(new Favorite { UserId = Guid.NewGuid(), AdId = ad1.Id });

        var ids = await new GetFavoriteIdsQueryHandler(_favorites).HandleAsync(new GetFavoriteIdsQuery(_luis.Id), CancellationToken.None);

        ids.Should().Equal(ad2.Id, ad1.Id);
    }

    [Theory]
    [InlineData(0, 20, false)]
    [InlineData(1, 101, false)]
    [InlineData(1, 20, true)]
    public void ListValidator_ChecksThePaging(int page, int pageSize, bool valid)
    {
        new GetFavoritesQueryValidator(new Tulaziva.Application.Localization.Localizer())
            .Validate(new GetFavoritesQuery(Guid.NewGuid(), page, pageSize)).IsValid.Should().Be(valid);
    }
}
