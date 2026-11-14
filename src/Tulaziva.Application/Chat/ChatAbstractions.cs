using Tulaziva.Domain.Entities;

namespace Tulaziva.Application.Chat;

/// <summary>A conversation as one of its participants sees it: with how many messages from the other one are still unread.</summary>
public sealed record ConversationSummary(Conversation Conversation, int UnreadCount);

public interface IConversationRepository
{
    /// <summary>The conversation with both participants, tracked so it can be changed and saved.</summary>
    Task<Conversation?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The conversation of this user about this ad, if it exists. With both participants.</summary>
    Task<Conversation?> FindAsync(Guid adId, Guid initiatorId, CancellationToken cancellationToken);

    /// <summary>The conversation as the given participant sees it. Null when it does not exist.</summary>
    Task<ConversationSummary?> GetSummaryAsync(Guid conversationId, Guid viewerId, CancellationToken cancellationToken);

    /// <summary>The conversations where the user is a participant, the most recently active first. Page is 1-based.</summary>
    Task<(IReadOnlyList<ConversationSummary> Items, int TotalCount)> GetForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>The messages of a conversation with their attachments, newest first. Page is 1-based.</summary>
    Task<(IReadOnlyList<Message> Items, int TotalCount)> GetMessagesAsync(Guid conversationId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Marks as read the messages the other participant sent. Changes are saved with SaveChangesAsync.</summary>
    Task MarkReadAsync(Guid conversationId, Guid readerId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Unread messages in all the conversations of the user.</summary>
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>An attachment with its message, and the conversation of the message.</summary>
    Task<MessageAttachment?> GetAttachmentAsync(Guid conversationId, Guid attachmentId, CancellationToken cancellationToken);

    /// <summary>Every attachment sent in the system, newest first, with its message and sender. For the admins; it never exposes the text.</summary>
    Task<(IReadOnlyList<MessageAttachment> Items, int TotalCount)> GetAttachmentsPageAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken);

    /// <summary>Adds a new message with its attachments. It goes through the repository for the same reason as the ad images: the ids are already set.</summary>
    void AddMessage(Message message);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
