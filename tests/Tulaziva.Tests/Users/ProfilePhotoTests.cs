using FluentAssertions;
using FluentValidation;
using Tulaziva.Application.Common;
using Tulaziva.Application.Localization;
using Tulaziva.Application.Users;
using Tulaziva.Domain.Entities;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Users;

public class ProfilePhotoTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeUserRepository _users = new();
    private readonly FakeFileStorage _storage = new();
    private readonly MutableTimeProvider _time = new(T0);
    private readonly User _ana = new() { Email = "ana@mail.com", DisplayName = "Ana" };

    public ProfilePhotoTests()
    {
        _users.Users.Add(_ana);
    }

    private SetProfilePhotoCommandHandler SetHandler() => new(_users, _storage, new Localizer(), _time);

    private Task<UserDto> Set(string fileName, byte[] bytes, Guid? userId = null, Stream? stream = null) =>
        SetHandler().HandleAsync(new SetProfilePhotoCommand(userId ?? _ana.Id, fileName, bytes.Length, stream ?? new MemoryStream(bytes)), CancellationToken.None);

    // ---- Upload ----

    [Theory]
    [InlineData("me.jpg", "image/jpeg", ".jpg")]
    [InlineData("me.JPEG", "image/jpeg", ".jpg")]
    [InlineData("me.png", "image/png", ".png")]
    [InlineData("C:\\Users\\ana\\Pictures\\me.PNG", "image/png", ".png")]
    public async Task Set_StoresThePhotoUnderTheProfileFolderOfTheUser_WithAServerChosenNameAndType(string fileName, string contentType, string extension)
    {
        var bytes = contentType == "image/png" ? TestImages.Png() : TestImages.Jpeg();

        var dto = await Set(fileName, bytes);

        var path = _storage.Files.Keys.Should().ContainSingle().Subject;
        path.Should().StartWith($"profiles/{_ana.Id:N}/").And.EndWith(extension).And.NotContain("me.");
        _storage.Files[path].Should().Equal(bytes);

        _ana.ProfilePhotoPath.Should().Be(path);
        _ana.ProfilePhotoContentType.Should().Be(contentType);
        _ana.ProfilePhotoUpdatedAtUtc.Should().Be(T0.UtcDateTime);
        dto.PhotoUrl.Should().Be($"/api/users/{_ana.Id}/photo?v={T0.UtcDateTime.Ticks}");
        _users.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Set_ANewPhoto_ReplacesTheOldOne_AndTheOldFileIsRemoved()
    {
        await Set("first.jpg", TestImages.Jpeg());
        var oldPath = _ana.ProfilePhotoPath!;
        _time.Advance(TimeSpan.FromMinutes(10));

        var dto = await Set("second.png", TestImages.Png());

        _storage.Files.Keys.Should().ContainSingle().Which.Should().NotBe(oldPath);
        _ana.ProfilePhotoContentType.Should().Be("image/png");
        dto.PhotoUrl.Should().EndWith($"v={T0.UtcDateTime.AddMinutes(10).Ticks}"); // a new URL, so no browser shows the old photo from its cache
    }

    [Fact]
    public async Task Set_WhenTheOldFileIsAlreadyGone_StillWorks()
    {
        _ana.ProfilePhotoPath = $"profiles/{_ana.Id:N}/missing.jpg";

        await Set("me.png", TestImages.Png());

        _storage.Files.Should().ContainSingle();
    }

    [Theory]
    [InlineData("contract.pdf")]
    [InlineData("archive.zip")]
    [InlineData("report.docx")]
    [InlineData("notes.txt")]
    [InlineData("anim.gif")]
    [InlineData("pic.webp")]
    [InlineData("setup.exe")]
    [InlineData("page.html")]
    [InlineData("noextension")]
    public async Task Set_AnythingThatIsNotAJpgOrPng_IsRefused_EvenWithTheBytesOfARealImage(string fileName)
    {
        var command = new SetProfilePhotoCommand(_ana.Id, fileName, 100, new MemoryStream(TestImages.Png()));

        var validation = await new SetProfilePhotoCommandValidator(new Localizer()).ValidateAsync(command);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.PropertyName == "FileName");

        // If it ever reached the handler, the handler refuses it too.
        await ((Func<Task>)(() => Set(fileName, TestImages.Png()))).Should().ThrowAsync<ValidationException>();
        _storage.Files.Should().BeEmpty();
        _ana.ProfilePhotoPath.Should().BeNull();
    }

    [Theory]
    [InlineData("me.png")]
    [InlineData("me.jpg")]
    public async Task Set_AProgramOrADocumentRenamedToAnImage_IsRefused(string fileName)
    {
        foreach (var disguised in new[] { TestFiles.Exe(), TestFiles.Pdf(), TestFiles.Zip(), TestFiles.Text() })
            await ((Func<Task>)(() => Set(fileName, disguised))).Should().ThrowAsync<ValidationException>();

        _storage.Files.Should().BeEmpty();
        _ana.ProfilePhotoPath.Should().BeNull();
    }

    [Fact]
    public async Task Set_APngWithTheExtensionOfAJpg_IsRefused()
    {
        await ((Func<Task>)(() => Set("me.jpg", TestImages.Png()))).Should().ThrowAsync<ValidationException>();
        await ((Func<Task>)(() => Set("me.png", TestImages.Jpeg()))).Should().ThrowAsync<ValidationException>();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(ProfilePhotoRules.MaxBytes, true)]
    [InlineData(ProfilePhotoRules.MaxBytes + 1, false)]
    public void TheValidator_LimitsTheSize(long length, bool valid)
    {
        var command = new SetProfilePhotoCommand(_ana.Id, "me.png", length, Stream.Null);

        new SetProfilePhotoCommandValidator(new Localizer()).Validate(command).IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task Set_FromAStreamThatCannotSeek_StoresTheWholeFile()
    {
        var bytes = TestImages.Png();

        await Set("me.png", bytes, stream: new NonSeekableStream(bytes));

        _storage.Files.Values.Should().ContainSingle().Which.Should().Equal(bytes);
    }

    [Fact]
    public async Task Set_WhenTheDatabaseFails_TheNewFileIsRemoved_AndTheOldPhotoStays()
    {
        await Set("old.png", TestImages.Png());
        var oldPath = _ana.ProfilePhotoPath!;
        _users.FailOnSave = true;

        await ((Func<Task>)(() => Set("new.jpg", TestImages.Jpeg()))).Should().ThrowAsync<InvalidOperationException>();

        _storage.Files.Keys.Should().Equal(oldPath);
    }

    [Fact]
    public async Task Set_ForAnUnknownOrDisabledUser_ThrowsNotFound()
    {
        _ana.IsActive = false;

        await ((Func<Task>)(() => Set("me.png", TestImages.Png()))).Should().ThrowAsync<NotFoundException>();
        await ((Func<Task>)(() => Set("me.png", TestImages.Png(), userId: Guid.NewGuid()))).Should().ThrowAsync<NotFoundException>();
        _storage.Files.Should().BeEmpty();
    }

    // ---- Remove ----

    [Fact]
    public async Task Remove_ClearsThePhotoAndDeletesTheFile()
    {
        await Set("me.png", TestImages.Png());

        var dto = await new RemoveProfilePhotoCommandHandler(_users, _storage)
            .HandleAsync(new RemoveProfilePhotoCommand(_ana.Id), CancellationToken.None);

        dto.PhotoUrl.Should().BeNull();
        _ana.ProfilePhotoPath.Should().BeNull();
        _ana.ProfilePhotoContentType.Should().BeNull();
        _ana.ProfilePhotoUpdatedAtUtc.Should().BeNull();
        _storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_WhenThereIsNoPhoto_IsHarmless()
    {
        var dto = await new RemoveProfilePhotoCommandHandler(_users, _storage)
            .HandleAsync(new RemoveProfilePhotoCommand(_ana.Id), CancellationToken.None);

        dto.PhotoUrl.Should().BeNull();
        _users.SaveCount.Should().Be(0);
    }

    // ---- Download ----

    private GetProfilePhotoQueryHandler GetHandler() => new(_users, _storage);

    [Fact]
    public async Task Get_ReturnsTheImageWithItsType()
    {
        await Set("me.png", TestImages.Png());

        var photo = await GetHandler().HandleAsync(new GetProfilePhotoQuery(_ana.Id), CancellationToken.None);

        photo.ContentType.Should().Be("image/png");
        using var memory = new MemoryStream();
        await photo.Content.CopyToAsync(memory);
        memory.ToArray().Should().Equal(TestImages.Png());
    }

    [Fact]
    public async Task Get_ForAUserWithoutPhoto_ADisabledUser_AnUnknownUser_OrAMissingFile_ThrowsNotFound()
    {
        var query = new GetProfilePhotoQuery(_ana.Id);

        await ((Func<Task>)(() => GetHandler().HandleAsync(query, CancellationToken.None))).Should().ThrowAsync<NotFoundException>();

        await Set("me.png", TestImages.Png());
        _storage.Files.Clear();
        await ((Func<Task>)(() => GetHandler().HandleAsync(query, CancellationToken.None))).Should().ThrowAsync<NotFoundException>();

        await ((Func<Task>)(() => GetHandler().HandleAsync(new GetProfilePhotoQuery(Guid.NewGuid()), CancellationToken.None))).Should().ThrowAsync<NotFoundException>();

        await Set("me.png", TestImages.Png());
        _ana.IsActive = false;
        await ((Func<Task>)(() => GetHandler().HandleAsync(query, CancellationToken.None))).Should().ThrowAsync<NotFoundException>();
    }

    // ---- Where the photo shows up ----

    [Fact]
    public void ThePhotoUrl_IsNullWithoutPhoto_AndChangesWithEachNewPhoto()
    {
        UserRoutes.Photo(_ana).Should().BeNull();

        _ana.ProfilePhotoPath = "profiles/x/a.png";
        _ana.ProfilePhotoUpdatedAtUtc = T0.UtcDateTime;
        var first = UserRoutes.Photo(_ana);

        _ana.ProfilePhotoUpdatedAtUtc = T0.UtcDateTime.AddSeconds(1);
        var second = UserRoutes.Photo(_ana);

        first.Should().StartWith($"/api/users/{_ana.Id}/photo?v=");
        second.Should().NotBe(first);
    }

    [Fact]
    public async Task TheProfilePhotoUrl_AppearsInTheUserTheAdOwnerAndTheChatParticipant()
    {
        await Set("me.png", TestImages.Png());
        var expected = UserRoutes.Photo(_ana);

        UserDto.From(_ana).PhotoUrl.Should().Be(expected);

        var luis = new User { Email = "luis@mail.com", DisplayName = "Luis" };
        var conversation = new Conversation { Initiator = luis, InitiatorId = luis.Id, Recipient = _ana, RecipientId = _ana.Id };
        var summary = new Application.Chat.ConversationSummary(conversation, 0);

        var dto = ChatMapperAccess.ToDto(summary, luis.Id);

        dto.OtherParticipant.PhotoUrl.Should().Be(expected);
        ChatMapperAccess.ToDto(summary, _ana.Id).OtherParticipant.PhotoUrl.Should().BeNull(); // Luis has no photo
    }
}

/// <summary>The chat mapper is internal to the application; this goes through the public handler that uses it.</summary>
internal static class ChatMapperAccess
{
    public static Application.Chat.ConversationDto ToDto(Application.Chat.ConversationSummary summary, Guid viewerId)
    {
        var repository = new FakeConversationRepository();
        repository.Conversations.Add(summary.Conversation);

        return new Application.Chat.GetConversationQueryHandler(repository)
            .HandleAsync(new Application.Chat.GetConversationQuery(viewerId, summary.Conversation.Id), CancellationToken.None)
            .GetAwaiter().GetResult();
    }
}
