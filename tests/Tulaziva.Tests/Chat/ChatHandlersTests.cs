using FluentAssertions;
using FluentValidation;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Chat;
using Tulaziva.Application.Common;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Chat;

public class ChatHandlersTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeUserRepository _users = new();
    private readonly FakeAdRepository _ads = new();
    private readonly FakeConversationRepository _conversations = new();
    private readonly FakeFileStorage _storage = new();
    private readonly MutableTimeProvider _time = new(T0);
    private readonly RecordingNotifier _notifier = new();

    private readonly User _ana = new() { Email = "ana@mail.com", DisplayName = "Ana" };     // owns the ad
    private readonly User _luis = new() { Email = "luis@mail.com", DisplayName = "Luis" };  // writes about it
    private readonly User _marta = new() { Email = "marta@mail.com", DisplayName = "Marta" }; // has nothing to do with it
    private readonly Ad _ad;
    private readonly FakeBlockRepository _blocks = new();

    public ChatHandlersTests()
    {
        _users.Users.AddRange(new[] { _ana, _luis, _marta });

        _ad = new Ad
        {
            OwnerId = _ana.Id,
            Owner = _ana,
            CategoryId = 1,
            Title = "Bicicleta de monta\u00f1a",
            ExpiresAtUtc = T0.UtcDateTime.AddDays(10)
        };
        _ads.Ads.Add(_ad);
    }

    // ---- Helpers ----

    private StartConversationCommandHandler StartHandler() => new(_ads, _users, _conversations, _time, _blocks);

    private SendMessageCommandHandler SendHandler() => new(_conversations, _storage, new Localizer(), _time, _notifier, _blocks);

    private async Task<ConversationDto> Start(User? user = null) =>
        (await StartHandler().HandleAsync(new StartConversationCommand((user ?? _luis).Id, _ad.Id), CancellationToken.None)).Conversation;

    private Task<MessageDto> Send(Guid conversationId, User sender, string? body, params IncomingFile[] files) =>
        SendHandler().HandleAsync(new SendMessageCommand(sender.Id, conversationId, body, files), CancellationToken.None);

    private static IncomingFile File(string name, byte[] bytes) => new(name, bytes.Length, new MemoryStream(bytes));

    // ---- Start ----

    [Fact]
    public async Task Start_OpensAChatBetweenTheUserAndTheOwnerOfTheAd()
    {
        var result = await StartHandler().HandleAsync(new StartConversationCommand(_luis.Id, _ad.Id), CancellationToken.None);

        result.Created.Should().BeTrue();
        result.Conversation.AdId.Should().Be(_ad.Id);
        result.Conversation.AdTitle.Should().Be("Bicicleta de monta\u00f1a");
        result.Conversation.OtherParticipant.Should().Be(new ParticipantDto(_ana.Id, "Ana"));
        result.Conversation.LastMessage.Should().BeNull();
        result.Conversation.UnreadCount.Should().Be(0);

        var conversation = _conversations.Conversations.Should().ContainSingle().Subject;
        conversation.InitiatorId.Should().Be(_luis.Id);
        conversation.RecipientId.Should().Be(_ana.Id);
    }

    [Fact]
    public async Task Start_ForTheSameAdAndUserAgain_ReturnsTheSameChat()
    {
        var first = await StartHandler().HandleAsync(new StartConversationCommand(_luis.Id, _ad.Id), CancellationToken.None);
        var second = await StartHandler().HandleAsync(new StartConversationCommand(_luis.Id, _ad.Id), CancellationToken.None);

        second.Created.Should().BeFalse();
        second.Conversation.Id.Should().Be(first.Conversation.Id);
        _conversations.Conversations.Should().ContainSingle();
    }

    [Fact]
    public async Task Start_EachUserWhoWritesAboutTheAdGetsTheirOwnChat()
    {
        var withLuis = await Start(_luis);
        var withMarta = await Start(_marta);

        withLuis.Id.Should().NotBe(withMarta.Id);
        _conversations.Conversations.Should().HaveCount(2);
    }

    [Fact]
    public async Task Start_OnYourOwnAd_ThrowsConflict()
    {
        var act = () => StartHandler().HandleAsync(new StartConversationCommand(_ana.Id, _ad.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ConflictException>();
        exception.Which.MessageKey.Should().Be(MessageKeys.CannotMessageYourself);
    }

    [Fact]
    public async Task Start_OnAnAdThatIsNotPublic_ThrowsNotFound()
    {
        _ad.Status = AdStatus.Removed;
        await ((Func<Task>)(() => Start())).Should().ThrowAsync<NotFoundException>();

        _ad.Status = AdStatus.Active;
        _ad.ExpiresAtUtc = T0.UtcDateTime.AddMinutes(-1);
        await ((Func<Task>)(() => Start())).Should().ThrowAsync<NotFoundException>();

        await ((Func<Task>)(() => StartHandler().HandleAsync(new StartConversationCommand(_luis.Id, Guid.NewGuid()), CancellationToken.None)))
            .Should().ThrowAsync<NotFoundException>();

        _conversations.Conversations.Should().BeEmpty();
    }

    [Fact]
    public async Task Start_ByAnUnknownOrDisabledUser_ThrowsNotFound()
    {
        _marta.IsActive = false;

        await ((Func<Task>)(() => Start(_marta))).Should().ThrowAsync<NotFoundException>();
        await ((Func<Task>)(() => StartHandler().HandleAsync(new StartConversationCommand(Guid.NewGuid(), _ad.Id), CancellationToken.None)))
            .Should().ThrowAsync<NotFoundException>();
    }

    // ---- Send ----

    [Fact]
    public async Task Send_SavesTheMessageAndUpdatesTheLastMessageOfTheChat()
    {
        var chat = await Start();
        _time.Advance(TimeSpan.FromMinutes(5));

        var message = await Send(chat.Id, _luis, "  Hola, sigue disponible?  ");

        message.Body.Should().Be("Hola, sigue disponible?");
        message.SenderId.Should().Be(_luis.Id);
        message.ReadAtUtc.Should().BeNull();
        message.Attachments.Should().BeEmpty();

        var conversation = _conversations.Conversations.Single();
        conversation.Messages.Should().ContainSingle();
        conversation.LastMessageSenderId.Should().Be(_luis.Id);
        conversation.LastMessagePreview.Should().Be("Hola, sigue disponible?");
        conversation.LastMessageHasAttachments.Should().BeFalse();
        conversation.LastMessageAtUtc.Should().Be(T0.UtcDateTime.AddMinutes(5));
    }

    [Fact]
    public async Task Send_ALongMessage_IsKeptWhole_AndTheListShowsATruncatedPreview()
    {
        var chat = await Start();
        var text = new string('a', ChatRules.MaxMessageLength);

        var message = await Send(chat.Id, _luis, text);

        message.Body.Should().HaveLength(ChatRules.MaxMessageLength);
        _conversations.Conversations.Single().LastMessagePreview.Should().HaveLength(ChatRules.PreviewLength);
    }

    [Fact]
    public async Task Send_ByAUserWhoIsNotInTheChat_ThrowsNotFoundAndSavesNothing()
    {
        var chat = await Start();

        var act = () => Send(chat.Id, _marta, "I am listening");

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.MessageKey.Should().Be(MessageKeys.ConversationNotFound);
        _conversations.Conversations.Single().Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Send_ToAnUnknownChat_ThrowsNotFound()
    {
        await ((Func<Task>)(() => Send(Guid.NewGuid(), _luis, "hi"))).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Send_Files_StoresThemUnderTheChatAndRecordsWhatTheServerDecided()
    {
        var chat = await Start();

        // The names say what the sender wants; the content type comes from the checked extension.
        var message = await Send(chat.Id, _luis, "El contrato", File("C:\\docs\\contrato final.jpg", TestImages.Jpeg()), File("foto.PNG", TestImages.Png()));

        message.Attachments.Should().HaveCount(2);

        var first = message.Attachments[0];
        first.FileName.Should().Be("contrato final.jpg");
        first.ContentType.Should().Be("image/jpeg");
        first.SizeBytes.Should().Be(TestImages.Jpeg().Length);
        first.Url.Should().Be(ChatRoutes.Attachment(chat.Id, first.Id));
        message.Attachments[1].ContentType.Should().Be("image/png");

        _storage.Files.Keys.Should().OnlyContain(path => path.StartsWith($"chat/{chat.Id:N}/"));
        _storage.Files.Keys.Should().Contain(path => path.EndsWith(".jpg")).And.Contain(path => path.EndsWith(".png"));
        _conversations.Conversations.Single().LastMessageHasAttachments.Should().BeTrue();
    }

    [Fact]
    public async Task Send_OnlyAFile_HasNoBody_AndThePreviewHasNoText()
    {
        var chat = await Start();

        var message = await Send(chat.Id, _luis, null, File("a.jpg", TestImages.Jpeg()));

        message.Body.Should().BeNull();
        var conversation = _conversations.Conversations.Single();
        conversation.LastMessagePreview.Should().BeNull();
        conversation.LastMessageHasAttachments.Should().BeTrue();
    }

    [Theory]
    [InlineData("contract.pdf")]
    [InlineData("archive.zip")]
    [InlineData("report.docx")]
    [InlineData("notes.txt")]
    [InlineData("anim.gif")]
    [InlineData("pic.webp")]
    [InlineData("setup.exe")]
    public async Task Send_ADocumentOrAnythingThatIsNotAJpgOrPng_IsRefusedAndNothingIsStored(string fileName)
    {
        var chat = await Start();

        // Not even with the bytes of a real image inside: the extension decides what can be sent.
        var command = new SendMessageCommand(_luis.Id, chat.Id, "mira", new[] { File(fileName, TestImages.Png()) });

        await DispatchWithValidation(command).Should().ThrowAsync<ValidationException>();

        _storage.Files.Should().BeEmpty();
        _conversations.Conversations.Single().Messages.Should().BeEmpty();
    }

    private Func<Task> DispatchWithValidation(SendMessageCommand command) => async () =>
    {
        // The dispatcher runs the validator before the handler; the same two steps here.
        var result = await _validator.ValidateAsync(command);

        if (!result.IsValid)
            throw new ValidationException(result.Errors);

        await SendHandler().HandleAsync(command, CancellationToken.None);
    };

    [Fact]
    public async Task Send_AnExecutableRenamedToJpg_IsRefusedAndNothingIsStored()
    {
        var chat = await Start();

        var act = () => Send(chat.Id, _luis, "mira", File("factura.jpg", TestFiles.Exe()));

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("factura.jpg");
        _storage.Files.Should().BeEmpty();
        _conversations.Conversations.Single().Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Send_WhenOneOfTheFilesIsBad_NoneOfThemIsStored()
    {
        var chat = await Start();

        var act = () => Send(chat.Id, _luis, null, File("good.jpg", TestImages.Jpeg()), File("bad.png", TestFiles.Exe()));

        await act.Should().ThrowAsync<ValidationException>();
        _storage.Files.Should().BeEmpty();
        _conversations.Conversations.Single().Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Send_AFileFromAStreamThatCannotSeek_IsStoredWhole()
    {
        var chat = await Start();
        var bytes = TestImages.Jpeg();

        await Send(chat.Id, _luis, null, new IncomingFile("a.jpg", bytes.Length, new NonSeekableStream(bytes)));

        _storage.Files.Values.Should().ContainSingle().Which.Should().Equal(bytes);
    }

    [Fact]
    public async Task Send_WhenTheDatabaseFails_TheStoredFilesAreRemoved()
    {
        var chat = await Start();
        _conversations.FailOnSave = true;

        var act = () => Send(chat.Id, _luis, "hi", File("a.jpg", TestImages.Jpeg()), File("b.png", TestImages.Png()));

        await act.Should().ThrowAsync<InvalidOperationException>();
        _storage.Files.Should().BeEmpty();
    }

    // ---- Validator ----

    private readonly SendMessageCommandValidator _validator = new(new Localizer());

    private static SendMessageCommand Command(string? body, params IncomingFile[] files) => new(Guid.NewGuid(), Guid.NewGuid(), body, files);

    [Fact]
    public void TheValidator_AcceptsTextFilesOrBoth()
    {
        _validator.Validate(Command("hi")).IsValid.Should().BeTrue();
        _validator.Validate(Command(null, File("a.jpg", TestImages.Jpeg()))).IsValid.Should().BeTrue();
        _validator.Validate(Command("hi", File("a.jpg", TestImages.Jpeg()))).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TheValidator_RefusesAMessageWithNeitherTextNorFiles(string? body)
    {
        _validator.Validate(Command(body)).Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Body");
    }

    [Fact]
    public void TheValidator_RefusesATextThatIsTooLong()
    {
        _validator.Validate(Command(new string('a', ChatRules.MaxMessageLength + 1))).IsValid.Should().BeFalse();
        _validator.Validate(Command(new string('a', ChatRules.MaxMessageLength))).IsValid.Should().BeTrue();
    }

    [Fact]
    public void TheValidator_RefusesMoreThanTheMaximumNumberOfFiles()
    {
        var files = Enumerable.Range(0, ChatRules.MaxFilesPerMessage + 1).Select(i => File($"f{i}.jpg", TestImages.Jpeg())).ToArray();

        _validator.Validate(Command("hi", files)).Errors.Should().Contain(e => e.PropertyName == "Files");
        _validator.Validate(Command("hi", files.Take(ChatRules.MaxFilesPerMessage).ToArray())).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("virus.exe", 100, false)]
    [InlineData("page.html", 100, false)]
    [InlineData("image.svg", 100, false)]
    [InlineData("script.js", 100, false)]
    [InlineData("contract.pdf", 100, false)]
    [InlineData("archive.zip", 100, false)]
    [InlineData("report.docx", 100, false)]
    [InlineData("report.doc", 100, false)]
    [InlineData("sheet.xlsx", 100, false)]
    [InlineData("notes.txt", 100, false)]
    [InlineData("data.csv", 100, false)]
    [InlineData("anim.gif", 100, false)]
    [InlineData("pic.webp", 100, false)]
    [InlineData("noextension", 100, false)]
    [InlineData("", 100, false)]
    [InlineData("a.jpg", 0, false)]
    [InlineData("a.jpg", ChatRules.MaxFileBytes, true)]
    [InlineData("a.jpg", ChatRules.MaxFileBytes + 1, false)]
    [InlineData("a.jpeg", 100, true)]
    [InlineData("a.png", 100, true)]
    [InlineData("A.JPG", 100, true)]
    [InlineData("A.PNG", 100, true)]
    public void TheValidator_ChecksTheNameAndTheSizeOfEachFile(string name, long length, bool valid)
    {
        var command = Command("hi", new IncomingFile(name, length, Stream.Null));

        _validator.Validate(command).IsValid.Should().Be(valid);
    }

    // ---- Lists and read state ----

    [Fact]
    public async Task GetMyConversations_ShowsEachUserTheOtherPersonAndTheirOwnUnreadCount()
    {
        var chat = await Start();
        _time.Advance(TimeSpan.FromMinutes(1));
        await Send(chat.Id, _luis, "uno");
        await Send(chat.Id, _luis, "dos");
        await Send(chat.Id, _ana, "tres");

        var handler = new GetMyConversationsQueryHandler(_conversations);
        var forAna = (await handler.HandleAsync(new GetMyConversationsQuery(_ana.Id), CancellationToken.None)).Items.Single();
        var forLuis = (await handler.HandleAsync(new GetMyConversationsQuery(_luis.Id), CancellationToken.None)).Items.Single();

        forAna.OtherParticipant.DisplayName.Should().Be("Luis");
        forAna.UnreadCount.Should().Be(2);
        forLuis.OtherParticipant.DisplayName.Should().Be("Ana");
        forLuis.UnreadCount.Should().Be(1);
        forLuis.LastMessage!.Text.Should().Be("tres");
        forLuis.LastMessage.SenderId.Should().Be(_ana.Id);
    }

    [Fact]
    public async Task GetMyConversations_ReturnsOnlyTheChatsOfTheUser_TheMostRecentFirst()
    {
        var older = await Start(_luis);
        _time.Advance(TimeSpan.FromHours(1));
        var newer = await Start(_marta);
        _time.Advance(TimeSpan.FromHours(1));
        await Send(older.Id, _luis, "bump");

        var handler = new GetMyConversationsQueryHandler(_conversations);
        var forAna = await handler.HandleAsync(new GetMyConversationsQuery(_ana.Id), CancellationToken.None);
        var forMarta = await handler.HandleAsync(new GetMyConversationsQuery(_marta.Id), CancellationToken.None);

        forAna.Items.Select(c => c.Id).Should().Equal(older.Id, newer.Id);
        forAna.TotalCount.Should().Be(2);
        forMarta.Items.Select(c => c.Id).Should().Equal(newer.Id);
    }

    [Fact]
    public async Task MarkRead_MarksOnlyTheMessagesTheOtherPersonSent()
    {
        var chat = await Start();
        await Send(chat.Id, _luis, "from luis");
        await Send(chat.Id, _ana, "from ana");
        _time.Advance(TimeSpan.FromMinutes(3));

        await new MarkConversationReadCommandHandler(_conversations, _time, _notifier)
            .HandleAsync(new MarkConversationReadCommand(_ana.Id, chat.Id), CancellationToken.None);

        var messages = _conversations.Conversations.Single().Messages;
        messages.Single(m => m.SenderId == _luis.Id).ReadAtUtc.Should().Be(T0.UtcDateTime.AddMinutes(3));
        messages.Single(m => m.SenderId == _ana.Id).ReadAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task UnreadCount_AddsUpAllTheChatsOfTheUser()
    {
        var withLuis = await Start(_luis);
        var withMarta = await Start(_marta);
        await Send(withLuis.Id, _luis, "1");
        await Send(withLuis.Id, _luis, "2");
        await Send(withMarta.Id, _marta, "3");
        await Send(withMarta.Id, _ana, "mine");

        var handler = new GetUnreadCountQueryHandler(_conversations);

        (await handler.HandleAsync(new GetUnreadCountQuery(_ana.Id), CancellationToken.None)).Count.Should().Be(3);
        (await handler.HandleAsync(new GetUnreadCountQuery(_marta.Id), CancellationToken.None)).Count.Should().Be(1);
        (await handler.HandleAsync(new GetUnreadCountQuery(_luis.Id), CancellationToken.None)).Count.Should().Be(0);
    }

    // ---- Privacy: the conversation ----

    [Fact]
    public async Task OnlyTheTwoParticipants_CanReadTheMessagesOfAChat()
    {
        var chat = await Start();
        await Send(chat.Id, _luis, "private words");
        var handler = new GetMessagesQueryHandler(_conversations);

        (await handler.HandleAsync(new GetMessagesQuery(_ana.Id, chat.Id), CancellationToken.None)).Items.Should().ContainSingle();
        (await handler.HandleAsync(new GetMessagesQuery(_luis.Id, chat.Id), CancellationToken.None)).Items.Should().ContainSingle();

        // A third user, and an admin who is not in the chat, get "not found": the chat is not even revealed to exist.
        var admin = new User { Email = "boss@mail.com", Role = UserRole.Admin };
        foreach (var outsider in new[] { _marta.Id, admin.Id, Guid.NewGuid() })
        {
            await ((Func<Task>)(() => handler.HandleAsync(new GetMessagesQuery(outsider, chat.Id), CancellationToken.None)))
                .Should().ThrowAsync<NotFoundException>();
            await ((Func<Task>)(() => new GetConversationQueryHandler(_conversations).HandleAsync(new GetConversationQuery(outsider, chat.Id), CancellationToken.None)))
                .Should().ThrowAsync<NotFoundException>();
            await ((Func<Task>)(() => new MarkConversationReadCommandHandler(_conversations, _time, _notifier).HandleAsync(new MarkConversationReadCommand(outsider, chat.Id), CancellationToken.None)))
                .Should().ThrowAsync<NotFoundException>();
        }
    }

    [Fact]
    public async Task GetMessages_ReturnsThemNewestFirst_ByPages()
    {
        var chat = await Start();
        for (var i = 1; i <= 5; i++)
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            await Send(chat.Id, _luis, $"m{i}");
        }

        var handler = new GetMessagesQueryHandler(_conversations);
        var page1 = await handler.HandleAsync(new GetMessagesQuery(_ana.Id, chat.Id, Page: 1, PageSize: 2), CancellationToken.None);
        var page3 = await handler.HandleAsync(new GetMessagesQuery(_ana.Id, chat.Id, Page: 3, PageSize: 2), CancellationToken.None);

        page1.TotalCount.Should().Be(5);
        page1.Items.Select(m => m.Body).Should().Equal("m5", "m4");
        page3.Items.Select(m => m.Body).Should().Equal("m1");
    }

    // ---- Privacy: the files ----

    private async Task<(ConversationDto Chat, AttachmentDto File)> ChatWithAFile()
    {
        var chat = await Start();
        var message = await Send(chat.Id, _luis, "te lo mando", File("contrato.jpg", TestImages.Jpeg()));

        return (chat, message.Attachments.Single());
    }

    private Task<AttachmentFile> Download(Guid chatId, Guid attachmentId, Requester requester) =>
        new GetAttachmentQueryHandler(_conversations, _storage).HandleAsync(new GetAttachmentQuery(chatId, attachmentId, requester), CancellationToken.None);

    [Fact]
    public async Task BothParticipants_AndTheAdmins_CanDownloadAFile()
    {
        var (chat, file) = await ChatWithAFile();

        var asAna = await Download(chat.Id, file.Id, new Requester(_ana.Id));
        var asLuis = await Download(chat.Id, file.Id, new Requester(_luis.Id));
        var asAdmin = await Download(chat.Id, file.Id, new Requester(_marta.Id, IsAdmin: true));

        asAna.FileName.Should().Be("contrato.jpg");
        asAna.ContentType.Should().Be("image/jpeg");
        asAna.IsImage.Should().BeTrue();
        asLuis.Should().NotBeNull();
        asAdmin.Should().NotBeNull();
    }

    [Fact]
    public async Task AnotherUser_AndAnonymous_CannotDownloadAFile()
    {
        var (chat, file) = await ChatWithAFile();

        await ((Func<Task>)(() => Download(chat.Id, file.Id, new Requester(_marta.Id)))).Should().ThrowAsync<NotFoundException>();
        await ((Func<Task>)(() => Download(chat.Id, file.Id, Requester.Anonymous))).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AFile_IsOnlyFoundInsideItsOwnChat()
    {
        var (_, file) = await ChatWithAFile();
        var other = await Start(_marta);

        await ((Func<Task>)(() => Download(other.Id, file.Id, new Requester(_marta.Id)))).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AnImageIsMarkedToBeShownInline_AndAMissingFileIsNotFound()
    {
        var chat = await Start();
        var message = await Send(chat.Id, _luis, null, File("foto.png", TestImages.Png()));
        var image = message.Attachments.Single();

        (await Download(chat.Id, image.Id, new Requester(_ana.Id))).IsImage.Should().BeTrue();

        _storage.Files.Clear();
        await ((Func<Task>)(() => Download(chat.Id, image.Id, new Requester(_ana.Id)))).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task TheAdminList_ShowsTheFilesAndWhoSentThem_NeverTheText()
    {
        var chat = await Start();
        await Send(chat.Id, _luis, "secret words that nobody else may read", File("contrato.jpg", TestImages.Jpeg()));

        var page = await new ListAttachmentsQueryHandler(_conversations).HandleAsync(new ListAttachmentsQuery(), CancellationToken.None);

        var item = page.Items.Should().ContainSingle().Subject;
        item.SenderId.Should().Be(_luis.Id);
        item.FileName.Should().Be("contrato.jpg");
        item.Url.Should().Be(ChatRoutes.Attachment(chat.Id, item.Id));
        System.Text.Json.JsonSerializer.Serialize(page).Should().NotContain("secret words");
    }

    // ---- Live notifications ----

    [Fact]
    public async Task Send_TellsTheOtherParticipantLive_WithTheNameOfTheSenderAndTheAd()
    {
        var chat = await Start();

        var message = await Send(chat.Id, _luis, "Hola, sigue disponible?");

        var (recipientId, notification) = _notifier.Messages.Should().ContainSingle().Subject;
        recipientId.Should().Be(_ana.Id);
        notification.Message.Should().Be(message);
        notification.SenderDisplayName.Should().Be("Luis");
        notification.AdTitle.Should().Be("Bicicleta de monta\u00f1a");
    }

    [Fact]
    public async Task Send_ByTheOwner_NotifiesTheUserWhoStartedTheChat()
    {
        var chat = await Start();

        await Send(chat.Id, _ana, "Si, sigue disponible");

        _notifier.Messages.Should().ContainSingle().Which.RecipientId.Should().Be(_luis.Id);
    }

    [Fact]
    public async Task Send_ARefusedMessage_NotifiesNobody()
    {
        var chat = await Start();

        var act = () => Send(chat.Id, _luis, "mira", File("foto.png", new byte[] { 1, 2, 3 }));

        await act.Should().ThrowAsync<Exception>();
        _notifier.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkRead_TellsTheOtherParticipantTheirMessagesWereRead()
    {
        var chat = await Start();
        await Send(chat.Id, _luis, "Hola");

        await new MarkConversationReadCommandHandler(_conversations, _time, _notifier)
            .HandleAsync(new MarkConversationReadCommand(_ana.Id, chat.Id), CancellationToken.None);

        _notifier.Reads.Should().ContainSingle().Which.Should().Be((_luis.Id, chat.Id));
    }
}
