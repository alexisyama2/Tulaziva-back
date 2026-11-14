using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tulaziva.Domain.Entities;
using Tulaziva.Infrastructure.Persistence;

namespace Tulaziva.Tests.Chat;

/// <summary>The chat queries against a real EF Core context (in memory): the filters, the order and the unread counts live there.</summary>
public class ConversationRepositoryTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly TulazivaDbContext _db;
    private readonly ConversationRepository _repository;

    private readonly User _ana = new() { Email = "ana@mail.com", DisplayName = "Ana" };
    private readonly User _luis = new() { Email = "luis@mail.com", DisplayName = "Luis" };
    private readonly User _marta = new() { Email = "marta@mail.com", DisplayName = "Marta" };

    public ConversationRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<TulazivaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new TulazivaDbContext(options);
        _db.Users.AddRange(_ana, _luis, _marta);
        _db.SaveChanges();
        _repository = new ConversationRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    private Conversation NewConversation(User initiator, User recipient, int lastMessageMinutes = 0, Guid? adId = null)
    {
        var conversation = new Conversation
        {
            AdId = adId ?? Guid.NewGuid(),
            AdTitle = "Bicicleta",
            InitiatorId = initiator.Id,
            RecipientId = recipient.Id,
            CreatedAtUtc = Now,
            LastMessageAtUtc = Now.AddMinutes(lastMessageMinutes)
        };

        _db.Conversations.Add(conversation);
        _db.SaveChanges();
        return conversation;
    }

    private Message NewMessage(Conversation conversation, User sender, string body, int minutes = 0, bool read = false)
    {
        var message = new Message
        {
            ConversationId = conversation.Id,
            SenderId = sender.Id,
            Body = body,
            CreatedAtUtc = Now.AddMinutes(minutes),
            ReadAtUtc = read ? Now.AddMinutes(minutes + 1) : null
        };

        _db.Messages.Add(message);
        _db.SaveChanges();
        return message;
    }

    [Fact]
    public async Task GetForUser_ReturnsOnlyTheChatsOfTheUser_TheMostRecentFirst_WithTheirParticipants()
    {
        var old = NewConversation(_luis, _ana, lastMessageMinutes: 1);
        var recent = NewConversation(_marta, _ana, lastMessageMinutes: 10);
        NewConversation(_luis, _marta, lastMessageMinutes: 99); // not Ana's

        var (items, total) = await _repository.GetForUserAsync(_ana.Id, 1, 10, CancellationToken.None);

        total.Should().Be(2);
        items.Select(i => i.Conversation.Id).Should().Equal(recent.Id, old.Id);
        items[0].Conversation.Initiator.DisplayName.Should().Be("Marta");
        items[0].Conversation.Recipient.DisplayName.Should().Be("Ana");
    }

    [Fact]
    public async Task GetForUser_CountsTheUnreadMessagesOfEachChatInOneGo_OnlyThoseFromTheOtherPerson()
    {
        var withLuis = NewConversation(_luis, _ana, 5);
        var withMarta = NewConversation(_marta, _ana, 1);

        NewMessage(withLuis, _luis, "1");
        NewMessage(withLuis, _luis, "2");
        NewMessage(withLuis, _luis, "read already", read: true);
        NewMessage(withLuis, _ana, "my own message");
        NewMessage(withMarta, _marta, "3");

        var (forAna, _) = await _repository.GetForUserAsync(_ana.Id, 1, 10, CancellationToken.None);
        var (forLuis, _) = await _repository.GetForUserAsync(_luis.Id, 1, 10, CancellationToken.None);

        forAna.Single(i => i.Conversation.Id == withLuis.Id).UnreadCount.Should().Be(2);
        forAna.Single(i => i.Conversation.Id == withMarta.Id).UnreadCount.Should().Be(1);
        forLuis.Single().UnreadCount.Should().Be(1); // only Ana's message
    }

    [Fact]
    public async Task GetForUser_PagesTheChats()
    {
        for (var i = 0; i < 5; i++)
            NewConversation(_luis, _ana, lastMessageMinutes: i);

        var (items, total) = await _repository.GetForUserAsync(_ana.Id, page: 3, pageSize: 2, CancellationToken.None);

        total.Should().Be(5);
        items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetSummary_ReturnsTheChatWithTheUnreadCountOfTheViewer_AndNullForAnUnknownOne()
    {
        var conversation = NewConversation(_luis, _ana);
        NewMessage(conversation, _luis, "hello");

        (await _repository.GetSummaryAsync(conversation.Id, _ana.Id, CancellationToken.None))!.UnreadCount.Should().Be(1);
        (await _repository.GetSummaryAsync(conversation.Id, _luis.Id, CancellationToken.None))!.UnreadCount.Should().Be(0);
        (await _repository.GetSummaryAsync(Guid.NewGuid(), _ana.Id, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task FindAndGetById_LoadTheParticipants()
    {
        var adId = Guid.NewGuid();
        var conversation = NewConversation(_luis, _ana, adId: adId);

        var byAd = await _repository.FindAsync(adId, _luis.Id, CancellationToken.None);
        var byId = await _repository.GetByIdAsync(conversation.Id, CancellationToken.None);

        byAd!.Id.Should().Be(conversation.Id);
        byId!.Recipient.DisplayName.Should().Be("Ana");
        (await _repository.FindAsync(adId, _marta.Id, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetMessages_ReturnsTheNewestFirst_WithTheirAttachments_ByPages()
    {
        var conversation = NewConversation(_luis, _ana);
        for (var i = 1; i <= 5; i++)
            NewMessage(conversation, _luis, $"m{i}", minutes: i);

        var last = await _db.Messages.SingleAsync(m => m.Body == "m5");
        _db.MessageAttachments.Add(new MessageAttachment { MessageId = last.Id, StoragePath = "chat/x/1.pdf", FileName = "a.pdf", ContentType = "application/pdf" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var (page1, total) = await _repository.GetMessagesAsync(conversation.Id, 1, 2, CancellationToken.None);
        var (page3, _) = await _repository.GetMessagesAsync(conversation.Id, 3, 2, CancellationToken.None);

        total.Should().Be(5);
        page1.Select(m => m.Body).Should().Equal("m5", "m4");
        page1[0].Attachments.Should().ContainSingle();
        page3.Select(m => m.Body).Should().Equal("m1");
    }

    [Fact]
    public async Task MarkRead_MarksOnlyTheMessagesOfTheOtherPerson_InThatChat()
    {
        var conversation = NewConversation(_luis, _ana);
        var other = NewConversation(_marta, _ana);
        NewMessage(conversation, _luis, "from luis");
        NewMessage(conversation, _ana, "from ana");
        NewMessage(other, _marta, "another chat");

        await _repository.MarkReadAsync(conversation.Id, _ana.Id, Now.AddHours(1), CancellationToken.None);
        await _repository.SaveChangesAsync(CancellationToken.None);
        _db.ChangeTracker.Clear();

        (await _db.Messages.SingleAsync(m => m.Body == "from luis")).ReadAtUtc.Should().Be(Now.AddHours(1));
        (await _db.Messages.SingleAsync(m => m.Body == "from ana")).ReadAtUtc.Should().BeNull();
        (await _db.Messages.SingleAsync(m => m.Body == "another chat")).ReadAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task GetUnreadCount_AddsUpTheChatsWhereTheUserParticipates_AndNothingElse()
    {
        var withLuis = NewConversation(_luis, _ana);
        var withMarta = NewConversation(_marta, _ana);
        var between = NewConversation(_luis, _marta);

        NewMessage(withLuis, _luis, "1");
        NewMessage(withLuis, _luis, "2");
        NewMessage(withMarta, _marta, "3");
        NewMessage(withMarta, _ana, "mine");
        NewMessage(between, _luis, "not for ana");

        (await _repository.GetUnreadCountAsync(_ana.Id, CancellationToken.None)).Should().Be(3);
        (await _repository.GetUnreadCountAsync(_marta.Id, CancellationToken.None)).Should().Be(2); // Ana's "mine" and Luis's "not for ana"
        (await _repository.GetUnreadCountAsync(Guid.NewGuid(), CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task AddMessage_SavesTheMessageAndItsAttachments_AsNewRows()
    {
        // The ids are already set. This is the case that EF takes for existing rows when the graph is not added explicitly.
        var conversation = NewConversation(_luis, _ana);
        var message = new Message { ConversationId = conversation.Id, SenderId = _luis.Id, Body = "with files", CreatedAtUtc = Now };
        message.Attachments.Add(new MessageAttachment { MessageId = message.Id, StoragePath = "chat/x/1.pdf", FileName = "a.pdf", ContentType = "application/pdf", SizeBytes = 10 });
        message.Attachments.Add(new MessageAttachment { MessageId = message.Id, StoragePath = "chat/x/2.png", FileName = "b.png", ContentType = "image/png", SizeBytes = 20 });

        _repository.AddMessage(message);
        await _repository.SaveChangesAsync(CancellationToken.None);

        (await _db.Messages.CountAsync()).Should().Be(1);
        (await _db.MessageAttachments.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task GetAttachment_ReturnsItWithItsConversation_OnlyForTheConversationItBelongsTo()
    {
        var conversation = NewConversation(_luis, _ana);
        var other = NewConversation(_marta, _ana);
        var message = NewMessage(conversation, _luis, "file");
        var attachment = new MessageAttachment { MessageId = message.Id, StoragePath = "chat/x/1.pdf", FileName = "a.pdf", ContentType = "application/pdf" };
        _db.MessageAttachments.Add(attachment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var found = await _repository.GetAttachmentAsync(conversation.Id, attachment.Id, CancellationToken.None);

        found!.Message.Conversation.HasParticipant(_ana.Id).Should().BeTrue();
        found.Message.Conversation.HasParticipant(_marta.Id).Should().BeFalse();
        (await _repository.GetAttachmentAsync(other.Id, attachment.Id, CancellationToken.None)).Should().BeNull();
        (await _repository.GetAttachmentAsync(conversation.Id, Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetAttachmentsPage_ReturnsEveryFileNewestFirst_WithTheSender()
    {
        var conversation = NewConversation(_luis, _ana);
        var first = NewMessage(conversation, _luis, "first");
        var second = NewMessage(conversation, _ana, "second");
        _db.MessageAttachments.AddRange(
            new MessageAttachment { MessageId = first.Id, StoragePath = "chat/x/1.pdf", FileName = "old.pdf", ContentType = "application/pdf", CreatedAtUtc = Now },
            new MessageAttachment { MessageId = second.Id, StoragePath = "chat/x/2.pdf", FileName = "new.pdf", ContentType = "application/pdf", CreatedAtUtc = Now.AddHours(1) });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var (items, total) = await _repository.GetAttachmentsPageAsync(1, 10, CancellationToken.None);

        total.Should().Be(2);
        items.Select(a => a.FileName).Should().Equal("new.pdf", "old.pdf");
        items[0].Message.Sender.DisplayName.Should().Be("Ana");
    }
}
