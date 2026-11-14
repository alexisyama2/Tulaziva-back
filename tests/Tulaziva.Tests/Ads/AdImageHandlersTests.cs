using FluentAssertions;
using FluentValidation;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Common;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Entities;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Ads;

public class AdImageHandlersTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeAdRepository _ads = new();
    private readonly FakeFileStorage _storage = new();
    private readonly TimeProvider _time = new FixedTimeProvider(Now);
    private readonly User _owner = new() { Email = "ana@mail.com", DisplayName = "Ana" };
    private readonly User _other = new() { Email = "luis@mail.com", DisplayName = "Luis" };
    private readonly Ad _ad;

    public AdImageHandlersTests()
    {
        _ad = new Ad { OwnerId = _owner.Id, Owner = _owner, CategoryId = 1, Title = "Bicicleta", ExpiresAtUtc = Now.UtcDateTime.AddDays(10) };
        _ads.Ads.Add(_ad);
    }

    private AddAdImageCommandHandler AddHandler() => new(_ads, _storage, new Localizer(), _time);

    private AddAdImageCommand AddCommand(byte[] bytes, Guid? userId = null, string fileName = "photo.jpg", Stream? stream = null) =>
        new(userId ?? _owner.Id, _ad.Id, fileName, bytes.Length, stream ?? new MemoryStream(bytes));

    // ---- Upload ----

    [Theory]
    [InlineData("jpeg", "image/jpeg", ".jpg")]
    [InlineData("png", "image/png", ".png")]
    [InlineData("webp", "image/webp", ".webp")]
    public async Task Add_StoresTheImageWithTheTypeOfItsContent(string kind, string contentType, string extension)
    {
        var bytes = kind switch { "jpeg" => TestImages.Jpeg(), "png" => TestImages.Png(), _ => TestImages.WebP() };

        // The name says .jpg whatever the content is: what counts is the content.
        var dto = await AddHandler().HandleAsync(AddCommand(bytes), CancellationToken.None);

        dto.ContentType.Should().Be(contentType);
        dto.SizeBytes.Should().Be(bytes.Length);
        dto.Url.Should().Be(AdRoutes.Image(_ad.Id, dto.Id));

        var path = _ad.Images.Should().ContainSingle().Subject.StoragePath;
        path.Should().StartWith($"ads/{_ad.Id:N}/").And.EndWith(extension);
        _storage.Files[path].Should().Equal(bytes);
        _ads.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Add_NumbersTheImagesInOrder()
    {
        var first = await AddHandler().HandleAsync(AddCommand(TestImages.Jpeg()), CancellationToken.None);
        var second = await AddHandler().HandleAsync(AddCommand(TestImages.Png(), fileName: "b.png"), CancellationToken.None);

        first.Position.Should().Be(0);
        second.Position.Should().Be(1);
    }

    [Fact]
    public async Task Add_FileThatIsNotAnImage_IsRejectedWhateverItsName()
    {
        var act = () => AddHandler().HandleAsync(AddCommand(TestImages.Text(), fileName: "photo.jpg"), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("File");
        _storage.Files.Should().BeEmpty();
        _ad.Images.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_EmptyFile_IsRejected()
    {
        var act = () => AddHandler().HandleAsync(AddCommand(Array.Empty<byte>()), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_WorksWithAStreamThatCannotSeek()
    {
        var bytes = TestImages.Png();

        await AddHandler().HandleAsync(AddCommand(bytes, fileName: "a.png", stream: new NonSeekableStream(bytes)), CancellationToken.None);

        _storage.Files.Values.Should().ContainSingle().Which.Should().Equal(bytes);
    }

    [Fact]
    public async Task Add_WhenTheAdAlreadyHasTheMaximum_ThrowsConflict()
    {
        for (var i = 0; i < AdRules.MaxImagesPerAd; i++)
            _ad.Images.Add(new AdImage { AdId = _ad.Id, Position = i });

        var act = () => AddHandler().HandleAsync(AddCommand(TestImages.Jpeg()), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ConflictException>();
        exception.Which.MessageKey.Should().Be(MessageKeys.TooManyImages);
        _storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_ToTheAdOfAnotherUser_ThrowsForbiddenAndStoresNothing()
    {
        var act = () => AddHandler().HandleAsync(AddCommand(TestImages.Jpeg(), userId: _other.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        _storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_ToARemovedAd_ThrowsConflict()
    {
        _ad.Status = Domain.Enums.AdStatus.Removed;

        var act = () => AddHandler().HandleAsync(AddCommand(TestImages.Jpeg()), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    // ---- Upload validator ----

    private readonly AddAdImageCommandValidator _validator = new(new Localizer());

    [Theory]
    [InlineData("photo.jpg", 100, true)]
    [InlineData("PHOTO.JPEG", 100, true)]
    [InlineData("photo.png", 100, true)]
    [InlineData("photo.webp", 100, true)]
    [InlineData("photo.gif", 100, false)]
    [InlineData("photo.exe", 100, false)]
    [InlineData("photo", 100, false)]
    [InlineData("", 100, false)]
    [InlineData("photo.jpg", 0, false)]
    [InlineData("photo.jpg", AdRules.MaxImageBytes, true)]
    [InlineData("photo.jpg", AdRules.MaxImageBytes + 1, false)]
    public void TheValidator_ChecksTheNameAndTheSize(string fileName, long length, bool valid)
    {
        var command = new AddAdImageCommand(Guid.NewGuid(), Guid.NewGuid(), fileName, length, Stream.Null);

        _validator.Validate(command).IsValid.Should().Be(valid);
    }

    // ---- Delete ----

    [Fact]
    public async Task DeleteImage_RemovesTheRowAndTheFile()
    {
        var dto = await AddHandler().HandleAsync(AddCommand(TestImages.Jpeg()), CancellationToken.None);

        await new DeleteAdImageCommandHandler(_ads, _storage)
            .HandleAsync(new DeleteAdImageCommand(_owner.Id, _ad.Id, dto.Id), CancellationToken.None);

        _ad.Images.Should().BeEmpty();
        _storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteImage_OfAnotherUser_ThrowsForbidden_AndAnUnknownImageNotFound()
    {
        var dto = await AddHandler().HandleAsync(AddCommand(TestImages.Jpeg()), CancellationToken.None);
        var handler = new DeleteAdImageCommandHandler(_ads, _storage);

        await ((Func<Task>)(() => handler.HandleAsync(new DeleteAdImageCommand(_other.Id, _ad.Id, dto.Id), CancellationToken.None)))
            .Should().ThrowAsync<ForbiddenException>();
        await ((Func<Task>)(() => handler.HandleAsync(new DeleteAdImageCommand(_owner.Id, _ad.Id, Guid.NewGuid()), CancellationToken.None)))
            .Should().ThrowAsync<NotFoundException>();

        _ad.Images.Should().ContainSingle();
        _storage.Files.Should().ContainSingle();
    }

    // ---- Download ----

    private GetAdImageQueryHandler GetHandler() => new(_ads, _storage, _time);

    [Fact]
    public async Task GetImage_OfAnActiveAd_IsPublic()
    {
        var dto = await AddHandler().HandleAsync(AddCommand(TestImages.Png(), fileName: "a.png"), CancellationToken.None);

        var file = await GetHandler().HandleAsync(new GetAdImageQuery(_ad.Id, dto.Id, Requester.Anonymous), CancellationToken.None);

        file.ContentType.Should().Be("image/png");
        using var memory = new MemoryStream();
        await file.Content.CopyToAsync(memory);
        memory.ToArray().Should().Equal(TestImages.Png());
    }

    [Fact]
    public async Task GetImage_OfAnExpiredAd_IsOnlyForItsOwnerAndTheAdmins()
    {
        var dto = await AddHandler().HandleAsync(AddCommand(TestImages.Jpeg()), CancellationToken.None);
        _ad.ExpiresAtUtc = Now.UtcDateTime.AddDays(-1);

        await ((Func<Task>)(() => GetHandler().HandleAsync(new GetAdImageQuery(_ad.Id, dto.Id, Requester.Anonymous), CancellationToken.None)))
            .Should().ThrowAsync<NotFoundException>();

        (await GetHandler().HandleAsync(new GetAdImageQuery(_ad.Id, dto.Id, new Requester(_owner.Id)), CancellationToken.None)).Should().NotBeNull();
        (await GetHandler().HandleAsync(new GetAdImageQuery(_ad.Id, dto.Id, new Requester(_other.Id, IsAdmin: true)), CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task GetImage_UnknownImageOrMissingFile_ThrowsNotFound()
    {
        var dto = await AddHandler().HandleAsync(AddCommand(TestImages.Jpeg()), CancellationToken.None);

        await ((Func<Task>)(() => GetHandler().HandleAsync(new GetAdImageQuery(_ad.Id, Guid.NewGuid(), Requester.Anonymous), CancellationToken.None)))
            .Should().ThrowAsync<NotFoundException>();

        _storage.Files.Clear();

        await ((Func<Task>)(() => GetHandler().HandleAsync(new GetAdImageQuery(_ad.Id, dto.Id, Requester.Anonymous), CancellationToken.None)))
            .Should().ThrowAsync<NotFoundException>();
    }
}
