using Tulaziva.Application.Chat;
using Tulaziva.Domain.Entities;

namespace Tulaziva.Tests.Fakes;

public sealed class FakeConversationRepository : IConversationRepository
{
    public List<Conversation> Conversations { get; } = new();

    public int SaveCount { get; private set; }

    /// <summary>Makes SaveChangesAsync fail, to check what the handlers clean up.</summary>
    public bool FailOnSave { get; set; }

    private IEnumerable<Message> AllMessages => Conversations.SelectMany(c => c.Messages);

    private static int Unread(Conversation conversation, Guid viewerId) =>
        conversation.Messages.Count(m => m.SenderId != viewerId && m.ReadAtUtc == null);

    public Task<Conversation?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Conversations.FirstOrDefault(c => c.Id == id));

    public Task<Conversation?> FindAsync(Guid adId, Guid initiatorId, CancellationToken cancellationToken) =>
        Task.FromResult(Conversations.FirstOrDefault(c => c.AdId == adId && c.InitiatorId == initiatorId));

    public Task<ConversationSummary?> GetSummaryAsync(Guid conversationId, Guid viewerId, CancellationToken cancellationToken)
    {
        var conversation = Conversations.FirstOrDefault(c => c.Id == conversationId);

        return Task.FromResult(conversation == null ? null : new ConversationSummary(conversation, Unread(conversation, viewerId)));
    }

    public Task<(IReadOnlyList<ConversationSummary> Items, int TotalCount)> GetForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var mine = Conversations.Where(c => c.HasParticipant(userId)).OrderByDescending(c => c.LastMessageAtUtc).ToList();

        IReadOnlyList<ConversationSummary> items = mine
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ConversationSummary(c, Unread(c, userId)))
            .ToList();

        return Task.FromResult((items, mine.Count));
    }

    public Task<(IReadOnlyList<Message> Items, int TotalCount)> GetMessagesAsync(Guid conversationId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var messages = Conversations.First(c => c.Id == conversationId).Messages.OrderByDescending(m => m.CreatedAtUtc).ToList();
        IReadOnlyList<Message> items = messages.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult((items, messages.Count));
    }

    public Task MarkReadAsync(Guid conversationId, Guid readerId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        foreach (var message in Conversations.First(c => c.Id == conversationId).Messages.Where(m => m.SenderId != readerId && m.ReadAtUtc == null))
            message.ReadAtUtc = nowUtc;

        return Task.CompletedTask;
    }

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Conversations.Where(c => c.HasParticipant(userId)).Sum(c => Unread(c, userId)));

    public Task<MessageAttachment?> GetAttachmentAsync(Guid conversationId, Guid attachmentId, CancellationToken cancellationToken) =>
        Task.FromResult(AllMessages
            .Where(m => m.ConversationId == conversationId)
            .SelectMany(m => m.Attachments)
            .FirstOrDefault(a => a.Id == attachmentId));

    public Task<(IReadOnlyList<MessageAttachment> Items, int TotalCount)> GetAttachmentsPageAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var all = AllMessages.SelectMany(m => m.Attachments).OrderByDescending(a => a.CreatedAtUtc).ToList();
        IReadOnlyList<MessageAttachment> items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult((items, all.Count));
    }

    public Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        Conversations.Add(conversation);
        return Task.CompletedTask;
    }

    public void AddMessage(Message message)
    {
        var conversation = Conversations.First(c => c.Id == message.ConversationId);

        // What EF fixes up by itself when the message is added.
        message.Conversation = conversation;
        foreach (var attachment in message.Attachments)
            attachment.Message = message;

        conversation.Messages.Add(message);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (FailOnSave)
            throw new InvalidOperationException("The database is down.");

        SaveCount++;
        return Task.CompletedTask;
    }
}

/// <summary>A clock the test moves by hand, to give messages different times.</summary>
public sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public MutableTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public void Advance(TimeSpan time) => _now += time;

    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>The first bytes of real files of the types the chat accepts, followed by padding.</summary>
public static class TestFiles
{
    public static byte[] Pdf() => Pad("%PDF-1.7\n"u8.ToArray());

    public static byte[] Zip() => Pad(new byte[] { 0x50, 0x4B, 0x03, 0x04 });

    public static byte[] Ole() => Pad(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 });

    public static byte[] Gif() => Pad("GIF89a"u8.ToArray());

    public static byte[] Text() => "hello, this is a plain text file\n"u8.ToArray();

    /// <summary>A Windows executable: starts with "MZ".</summary>
    public static byte[] Exe() => Pad(new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 });

    public static byte[] Padded(byte[] header) => Pad(header);

    private static byte[] Pad(byte[] header)
    {
        var bytes = new byte[96];
        header.CopyTo(bytes, 0);
        return bytes;
    }
}
