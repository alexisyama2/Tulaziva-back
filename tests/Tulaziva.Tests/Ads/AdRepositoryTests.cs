using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tulaziva.Application.Ads;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;
using Tulaziva.Infrastructure.Persistence;

namespace Tulaziva.Tests.Ads;

/// <summary>The search against a real EF Core context (in memory), since that is where the filters and the distance order live.</summary>
public class AdRepositoryTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    // Mexico City, with other cities at known distances.
    private const double MexicoLat = 19.4326, MexicoLon = -99.1332;
    private const double PueblaLat = 19.0414, PueblaLon = -98.2063;        // ~ 110 km
    private const double GuadalajaraLat = 20.6597, GuadalajaraLon = -103.3496; // ~ 460 km

    private readonly TulazivaDbContext _db;
    private readonly AdRepository _repository;
    private readonly User _owner = new() { Email = "ana@mail.com", DisplayName = "Ana" };

    public AdRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<TulazivaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new TulazivaDbContext(options);
        _db.Database.EnsureCreated(); // also loads the seeded categories
        _db.Users.Add(_owner);
        _db.SaveChanges();
        _repository = new AdRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    private Ad Add(string title, int daysOld = 0, int categoryId = 1, decimal price = 100, double? lat = null, double? lon = null,
        AdStatus status = AdStatus.Active, int expiresInDays = 10, string description = "d")
    {
        var ad = new Ad
        {
            OwnerId = _owner.Id,
            CategoryId = categoryId,
            Title = title,
            Description = description,
            Price = price,
            Currency = "MXN",
            Latitude = lat,
            Longitude = lon,
            Status = status,
            CreatedAtUtc = Now.AddDays(-daysOld),
            UpdatedAtUtc = Now.AddDays(-daysOld),
            ExpiresAtUtc = Now.AddDays(expiresInDays)
        };

        _db.Ads.Add(ad);
        _db.SaveChanges();
        return ad;
    }

    private static AdSearchCriteria Criteria(string? text = null, int? category = null, decimal? min = null, decimal? max = null,
        double? lat = null, double? lon = null, double? radius = null, int page = 1, int pageSize = 20) =>
        new(text, category, min, max, lat, lon, radius, page, pageSize);

    private async Task<string[]> Titles(AdSearchCriteria criteria)
    {
        var (items, _) = await _repository.SearchAsync(criteria, Now, CancellationToken.None);
        return items.Select(i => i.Ad.Title).ToArray();
    }

    [Fact]
    public async Task Search_ReturnsTheActiveAdsThatHaveNotExpired_NewestFirst()
    {
        Add("old", daysOld: 5);
        Add("new", daysOld: 1);
        Add("removed", status: AdStatus.Removed);
        Add("expired", expiresInDays: -1);

        (await Titles(Criteria())).Should().Equal("new", "old");
    }

    [Fact]
    public async Task Search_FindsTheTextInTheTitleOrTheDescription()
    {
        Add("Bicicleta de montaña");
        Add("Casco", description: "para bicicleta");
        Add("Mesa");

        (await Titles(Criteria(text: "bicicleta"))).Should().BeEquivalentTo("Bicicleta de montaña", "Casco");
    }

    [Fact]
    public async Task Search_FiltersByCategoryAndPrice()
    {
        Add("cheap car", categoryId: 1, price: 50);
        Add("mid car", categoryId: 1, price: 500);
        Add("mid job", categoryId: 3, price: 500);
        Add("dear car", categoryId: 1, price: 5000);

        (await Titles(Criteria(category: 1, min: 100, max: 1000))).Should().Equal("mid car");
    }

    [Fact]
    public async Task Search_PagesTheResultsAndReportsTheTotal()
    {
        for (var i = 0; i < 5; i++)
            Add($"ad{i}", daysOld: i);

        var (items, total) = await _repository.SearchAsync(Criteria(page: 2, pageSize: 2), Now, CancellationToken.None);

        total.Should().Be(5);
        items.Select(i => i.Ad.Title).Should().Equal("ad2", "ad3");
        items.Should().OnlyContain(i => i.DistanceKm == null);
    }

    [Fact]
    public async Task Search_WithALocation_OrdersByDistanceAndReportsIt()
    {
        Add("guadalajara", lat: GuadalajaraLat, lon: GuadalajaraLon);
        Add("mexico", lat: MexicoLat, lon: MexicoLon);
        Add("puebla", lat: PueblaLat, lon: PueblaLon);

        var (items, total) = await _repository.SearchAsync(Criteria(lat: MexicoLat, lon: MexicoLon), Now, CancellationToken.None);

        total.Should().Be(3);
        items.Select(i => i.Ad.Title).Should().Equal("mexico", "puebla", "guadalajara");
        items[0].DistanceKm.Should().BeApproximately(0, 0.01);
        items[1].DistanceKm.Should().BeApproximately(110, 10);
        items[2].DistanceKm.Should().BeApproximately(461, 10);
    }

    [Fact]
    public async Task Search_WithARadius_LeavesOutTheFartherAdsAndThoseWithoutLocation()
    {
        Add("guadalajara", lat: GuadalajaraLat, lon: GuadalajaraLon);
        Add("puebla", lat: PueblaLat, lon: PueblaLon);
        Add("nowhere");

        (await Titles(Criteria(lat: MexicoLat, lon: MexicoLon, radius: 200))).Should().Equal("puebla");
        (await Titles(Criteria(lat: MexicoLat, lon: MexicoLon, radius: 50))).Should().BeEmpty();
        (await Titles(Criteria(lat: MexicoLat, lon: MexicoLon, radius: 500))).Should().Equal("puebla", "guadalajara");
    }

    [Fact]
    public async Task Search_WithALocationButNoRadius_KeepsTheAdsWithoutLocationAtTheEnd()
    {
        Add("nowhere");
        Add("puebla", lat: PueblaLat, lon: PueblaLon);

        (await Titles(Criteria(lat: MexicoLat, lon: MexicoLon))).Should().Equal("puebla", "nowhere");
    }

    [Fact]
    public async Task Search_WithALocation_PagesAfterOrderingByDistance()
    {
        Add("guadalajara", lat: GuadalajaraLat, lon: GuadalajaraLon);
        Add("mexico", lat: MexicoLat, lon: MexicoLon);
        Add("puebla", lat: PueblaLat, lon: PueblaLon);

        var (items, total) = await _repository.SearchAsync(Criteria(lat: MexicoLat, lon: MexicoLon, page: 2, pageSize: 2), Now, CancellationToken.None);

        total.Should().Be(3);
        items.Select(i => i.Ad.Title).Should().Equal("guadalajara");
    }

    [Fact]
    public async Task GetByOwner_ReturnsTheAdsOfThatUserWhateverTheirStatus_NewestFirst()
    {
        Add("old", daysOld: 3);
        Add("removed", daysOld: 1, status: AdStatus.Removed);
        Add("expired", daysOld: 2, expiresInDays: -1);

        var other = new User { Email = "luis@mail.com", DisplayName = "Luis" };
        _db.Users.Add(other);
        _db.Ads.Add(new Ad { OwnerId = other.Id, CategoryId = 1, Title = "not mine", ExpiresAtUtc = Now.AddDays(1) });
        await _db.SaveChangesAsync();

        var (items, total) = await _repository.GetByOwnerAsync(_owner.Id, 1, 10, CancellationToken.None);

        total.Should().Be(3);
        items.Select(a => a.Title).Should().Equal("removed", "expired", "old");
    }

    [Fact]
    public async Task GetById_LoadsTheImagesAndTheOwner()
    {
        var ad = Add("bici");
        _db.AdImages.Add(new AdImage { AdId = ad.Id, StoragePath = "ads/x/1.jpg", ContentType = "image/jpeg" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var loaded = await _repository.GetByIdAsync(ad.Id, CancellationToken.None);

        loaded!.Images.Should().ContainSingle();
        loaded.Owner.DisplayName.Should().Be("Ana");
    }

    [Fact]
    public async Task RemoveImage_AndRemoveAd_DeleteTheRows()
    {
        var ad = Add("bici");
        var image = new AdImage { AdId = ad.Id, StoragePath = "ads/x/1.jpg", ContentType = "image/jpeg" };
        _db.AdImages.Add(image);
        await _db.SaveChangesAsync();

        _repository.RemoveImage(image);
        await _repository.SaveChangesAsync(CancellationToken.None);
        (await _db.AdImages.CountAsync()).Should().Be(0);

        _repository.Remove(ad);
        await _repository.SaveChangesAsync(CancellationToken.None);
        (await _db.Ads.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TheCategoriesAreSeeded()
    {
        var categories = await new CategoryRepository(_db).GetAllAsync(CancellationToken.None);

        categories.Select(c => c.Key).Should().Equal(DefaultCategories.All.Select(c => c.Key));
        (await new CategoryRepository(_db).ExistsAsync(1, CancellationToken.None)).Should().BeTrue();
        (await new CategoryRepository(_db).ExistsAsync(999, CancellationToken.None)).Should().BeFalse();
    }
}
