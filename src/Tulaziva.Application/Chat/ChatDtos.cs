using Tulaziva.Domain.Entities;

namespace Tulaziva.Application.Chat;

public sealed record ParticipantDto(Guid Id, string DisplayName, string? PhotoUrl = null, bool Deleted = false);

public sealed record MessagePreviewDto(Guid SenderId, string? Text, bool HasAttachments, DateTime CreatedAtUtc);

public sealed record ConversationDto(
    Guid Id,
    Guid AdId,
    string AdTitle,
    ParticipantDto OtherParticipant,
    MessagePreviewDto? LastMessage,
    int UnreadCount,
    DateTime CreatedAtUtc,
    DateTime LastMessageAtUtc);

public sealed record AttachmentDto(Guid Id, string FileName, string ContentType, long SizeBytes, string Url);

public sealed record MessageDto(
    Guid Id,
    Guid ConversationId,
    Guid SenderId,
    string? Body,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc,
    IReadOnlyList<AttachmentDto> Attachments);

public sealed record UnreadCountDto(int Count);

/// <summary>What an admin sees of a file: who sent it and when, never the text of the conversation.</summary>
public sealed record AdminAttachmentDto(
    Guid Id,
    Guid ConversationId,
    Guid SenderId,
    string SenderDisplayName,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAtUtc,
    string Url);

public sealed record AttachmentFile(Stream Content, string ContentType, string FileName, bool IsImage);

/// <summary>A file that arrives with a message. Length is what the server measured.</summary>
public sealed record IncomingFile(string FileName, long Length, Stream Content);

public static class ChatRoutes
{
    public static string Attachment(Guid conversationId, Guid attachmentId) =>
        $"/api/conversations/{conversationId}/attachments/{attachmentId}";
}

internal static class ChatMapper
{
    public static AttachmentDto ToDto(Guid conversationId, MessageAttachment attachment) =>
        new(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, ChatRoutes.Attachment(conversationId, attachment.Id));

    public static MessageDto ToDto(Message message) => new(
        message.Id,
        message.ConversationId,
        message.SenderId,
        message.Body,
        message.CreatedAtUtc,
        message.ReadAtUtc,
        message.Attachments.Select(a => ToDto(message.ConversationId, a)).ToList());

    public static ConversationDto ToDto(ConversationSummary summary, Guid viewerId)
    {
        var conversation = summary.Conversation;
        var other = conversation.OtherParticipant(viewerId);

        MessagePreviewDto? last = conversation.LastMessageSenderId.HasValue
            ? new MessagePreviewDto(conversation.LastMessageSenderId.Value, conversation.LastMessagePreview, conversation.LastMessageHasAttachments, conversation.LastMessageAtUtc)
            : null;

        return new ConversationDto(
            conversation.Id,
            conversation.AdId,
            conversation.AdTitle,
            new ParticipantDto(other.Id, other.DisplayName, Users.UserRoutes.Photo(other), other.DeletedAtUtc.HasValue),
            last,
            summary.UnreadCount,
            conversation.CreatedAtUtc,
            conversation.LastMessageAtUtc);
    }

    public static AdminAttachmentDto ToAdminDto(MessageAttachment attachment) => new(
        attachment.Id,
        attachment.Message.ConversationId,
        attachment.Message.SenderId,
        attachment.Message.Sender?.DisplayName ?? string.Empty,
        attachment.FileName,
        attachment.ContentType,
        attachment.SizeBytes,
        attachment.CreatedAtUtc,
        ChatRoutes.Attachment(attachment.Message.ConversationId, attachment.Id));
}

internal static class ChatAccess
{
    /// <summary>
    /// The conversation, only when the user is one of its two participants. Anyone else, an admin included, gets "not found":
    /// the conversation is not revealed to exist.
    /// </summary>
    public static async Task<Conversation> GetForParticipantAsync(
        IConversationRepository conversations, Guid conversationId, Guid userId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetByIdAsync(conversationId, cancellationToken);

        if (conversation == null || !conversation.HasParticipant(userId))
            throw new Common.NotFoundException(Localization.MessageKeys.ConversationNotFound);

        return conversation;
    }
}
