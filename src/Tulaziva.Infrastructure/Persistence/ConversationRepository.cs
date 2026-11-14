using Microsoft.EntityFrameworkCore;
using Tulaziva.Application.Chat;
using Tulaziva.Domain.Entities;

namespace Tulaziva.Infrastructure.Persistence;

public sealed class ConversationRepository : IConversationRepository
{
    private readonly TulazivaDbContext _db;

    public ConversationRepository(TulazivaDbContext db)
    {
        _db = db;
    }

    public Task<Conversation?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Conversations
            .Include(c => c.Initiator)
            .Include(c => c.Recipient)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Conversation?> FindAsync(Guid adId, Guid initiatorId, CancellationToken cancellationToken) =>
        _db.Conversations
            .Include(c => c.Initiator)
            .Include(c => c.Recipient)
            .FirstOrDefaultAsync(c => c.AdId == adId && c.InitiatorId == initiatorId, cancellationToken);

    public async Task<ConversationSummary?> GetSummaryAsync(Guid conversationId, Guid viewerId, CancellationToken cancellationToken)
    {
        var conversation = await _db.Conversations
            .AsNoTracking()
            .Include(c => c.Initiator)
            .Include(c => c.Recipient)
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (conversation == null)
            return null;

        var unread = await _db.Messages.CountAsync(
            m => m.ConversationId == conversationId && m.SenderId != viewerId && m.ReadAtUtc == null, cancellationToken);

        return new ConversationSummary(conversation, unread);
    }

    public async Task<(IReadOnlyList<ConversationSummary> Items, int TotalCount)> GetForUserAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _db.Conversations
            .AsNoTracking()
            .Where(c => c.InitiatorId == userId || c.RecipientId == userId);

        var totalCount = await query.CountAsync(cancellationToken);

        var conversations = await query
            .Include(c => c.Initiator)
            .Include(c => c.Recipient)
            .OrderByDescending(c => c.LastMessageAtUtc)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var ids = conversations.Select(c => c.Id).ToList();

        // One query for the unread counts of the whole page, not one per conversation.
        var unreadByConversation = await _db.Messages
            .Where(m => ids.Contains(m.ConversationId) && m.SenderId != userId && m.ReadAtUtc == null)
            .GroupBy(m => m.ConversationId)
            .Select(g => new { ConversationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ConversationId, x => x.Count, cancellationToken);

        var items = conversations
            .Select(c => new ConversationSummary(c, unreadByConversation.GetValueOrDefault(c.Id)))
            .ToList();

        return (items, totalCount);
    }

    public async Task<(IReadOnlyList<Message> Items, int TotalCount)> GetMessagesAsync(
        Guid conversationId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _db.Messages.AsNoTracking().Where(m => m.ConversationId == conversationId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Include(m => m.Attachments)
            .OrderByDescending(m => m.CreatedAtUtc)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task MarkReadAsync(Guid conversationId, Guid readerId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var unread = await _db.Messages
            .Where(m => m.ConversationId == conversationId && m.SenderId != readerId && m.ReadAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var message in unread)
            message.ReadAtUtc = nowUtc;
    }

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken) =>
        _db.Messages.CountAsync(
            m => m.SenderId != userId
                 && m.ReadAtUtc == null
                 && (m.Conversation.InitiatorId == userId || m.Conversation.RecipientId == userId),
            cancellationToken);

    public Task<MessageAttachment?> GetAttachmentAsync(Guid conversationId, Guid attachmentId, CancellationToken cancellationToken) =>
        _db.MessageAttachments
            .AsNoTracking()
            .Include(a => a.Message).ThenInclude(m => m.Conversation)
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.Message.ConversationId == conversationId, cancellationToken);

    public async Task<(IReadOnlyList<MessageAttachment> Items, int TotalCount)> GetAttachmentsPageAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _db.MessageAttachments.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Include(a => a.Message).ThenInclude(m => m.Sender)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken) =>
        await _db.Conversations.AddAsync(conversation, cancellationToken);

    public void AddMessage(Message message) => _db.Messages.Add(message);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
