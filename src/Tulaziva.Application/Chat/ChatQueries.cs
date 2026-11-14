using FluentValidation;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;

namespace Tulaziva.Application.Chat;

// ---- List of chats ----

public sealed record GetMyConversationsQuery(Guid UserId, int Page = 1, int PageSize = 20) : IQuery<PagedResult<ConversationDto>>;

public sealed class GetMyConversationsQueryValidator : AbstractValidator<GetMyConversationsQuery>
{
    public GetMyConversationsQueryValidator(ILocalizer localizer)
    {
        this.AddPagingRules(localizer, x => x.Page, x => x.PageSize);
    }
}

public sealed class GetMyConversationsQueryHandler : IQueryHandler<GetMyConversationsQuery, PagedResult<ConversationDto>>
{
    private readonly IConversationRepository _conversations;

    public GetMyConversationsQueryHandler(IConversationRepository conversations)
    {
        _conversations = conversations;
    }

    public async Task<PagedResult<ConversationDto>> HandleAsync(GetMyConversationsQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _conversations.GetForUserAsync(query.UserId, query.Page, query.PageSize, cancellationToken);

        return new PagedResult<ConversationDto>(items.Select(s => ChatMapper.ToDto(s, query.UserId)).ToList(), query.Page, query.PageSize, totalCount);
    }
}

// ---- One chat ----

public sealed record GetConversationQuery(Guid UserId, Guid ConversationId) : IQuery<ConversationDto>;

public sealed class GetConversationQueryHandler : IQueryHandler<GetConversationQuery, ConversationDto>
{
    private readonly IConversationRepository _conversations;

    public GetConversationQueryHandler(IConversationRepository conversations)
    {
        _conversations = conversations;
    }

    public async Task<ConversationDto> HandleAsync(GetConversationQuery query, CancellationToken cancellationToken)
    {
        var conversation = await ChatAccess.GetForParticipantAsync(_conversations, query.ConversationId, query.UserId, cancellationToken);
        var summary = await _conversations.GetSummaryAsync(conversation.Id, query.UserId, cancellationToken) ?? new ConversationSummary(conversation, 0);

        return ChatMapper.ToDto(summary, query.UserId);
    }
}

// ---- Messages of a chat ----

/// <summary>The messages of a conversation, newest first. Reading them does not mark them as read: that is MarkConversationReadCommand.</summary>
public sealed record GetMessagesQuery(Guid UserId, Guid ConversationId, int Page = 1, int PageSize = 30) : IQuery<PagedResult<MessageDto>>;

public sealed class GetMessagesQueryValidator : AbstractValidator<GetMessagesQuery>
{
    public GetMessagesQueryValidator(ILocalizer localizer)
    {
        this.AddPagingRules(localizer, x => x.Page, x => x.PageSize);
    }
}

public sealed class GetMessagesQueryHandler : IQueryHandler<GetMessagesQuery, PagedResult<MessageDto>>
{
    private readonly IConversationRepository _conversations;

    public GetMessagesQueryHandler(IConversationRepository conversations)
    {
        _conversations = conversations;
    }

    public async Task<PagedResult<MessageDto>> HandleAsync(GetMessagesQuery query, CancellationToken cancellationToken)
    {
        await ChatAccess.GetForParticipantAsync(_conversations, query.ConversationId, query.UserId, cancellationToken);

        var (items, totalCount) = await _conversations.GetMessagesAsync(query.ConversationId, query.Page, query.PageSize, cancellationToken);

        return new PagedResult<MessageDto>(items.Select(ChatMapper.ToDto).ToList(), query.Page, query.PageSize, totalCount);
    }
}

// ---- Read state ----

/// <summary>Marks as read the messages the other participant sent in this conversation.</summary>
public sealed record MarkConversationReadCommand(Guid UserId, Guid ConversationId) : ICommand<Unit>;

public sealed class MarkConversationReadCommandHandler : ICommandHandler<MarkConversationReadCommand, Unit>
{
    private readonly IConversationRepository _conversations;
    private readonly TimeProvider _time;
    private readonly IRealtimeNotifier _notifier;

    public MarkConversationReadCommandHandler(IConversationRepository conversations, TimeProvider time, IRealtimeNotifier notifier)
    {
        _notifier = notifier;
        _conversations = conversations;
        _time = time;
    }

    public async Task<Unit> HandleAsync(MarkConversationReadCommand command, CancellationToken cancellationToken)
    {
        var conversation = await ChatAccess.GetForParticipantAsync(_conversations, command.ConversationId, command.UserId, cancellationToken);

        await _conversations.MarkReadAsync(command.ConversationId, command.UserId, _time.GetUtcNow().UtcDateTime, cancellationToken);
        await _conversations.SaveChangesAsync(cancellationToken);

        // The other participant sees their messages as read.
        await _notifier.ConversationReadAsync(conversation.OtherParticipant(command.UserId).Id, conversation.Id, cancellationToken);

        return default;
    }
}

public sealed record GetUnreadCountQuery(Guid UserId) : IQuery<UnreadCountDto>;

public sealed class GetUnreadCountQueryHandler : IQueryHandler<GetUnreadCountQuery, UnreadCountDto>
{
    private readonly IConversationRepository _conversations;

    public GetUnreadCountQueryHandler(IConversationRepository conversations)
    {
        _conversations = conversations;
    }

    public async Task<UnreadCountDto> HandleAsync(GetUnreadCountQuery query, CancellationToken cancellationToken) =>
        new(await _conversations.GetUnreadCountAsync(query.UserId, cancellationToken));
}

// ---- Files ----

/// <summary>
/// A file of a conversation. The two participants can download it, and so can the admins (to moderate the files).
/// To anyone else it does not exist.
/// </summary>
public sealed record GetAttachmentQuery(Guid ConversationId, Guid AttachmentId, Requester Requester) : IQuery<AttachmentFile>;

public sealed class GetAttachmentQueryHandler : IQueryHandler<GetAttachmentQuery, AttachmentFile>
{
    private readonly IConversationRepository _conversations;
    private readonly IFileStorage _storage;

    public GetAttachmentQueryHandler(IConversationRepository conversations, IFileStorage storage)
    {
        _conversations = conversations;
        _storage = storage;
    }

    public async Task<AttachmentFile> HandleAsync(GetAttachmentQuery query, CancellationToken cancellationToken)
    {
        var attachment = await _conversations.GetAttachmentAsync(query.ConversationId, query.AttachmentId, cancellationToken)
            ?? throw new NotFoundException(MessageKeys.AttachmentNotFound);

        var isParticipant = query.Requester.UserId.HasValue && attachment.Message.Conversation.HasParticipant(query.Requester.UserId.Value);

        if (!isParticipant && !query.Requester.IsAdmin)
            throw new NotFoundException(MessageKeys.AttachmentNotFound);

        var stream = await _storage.OpenReadAsync(attachment.StoragePath, cancellationToken)
            ?? throw new NotFoundException(MessageKeys.AttachmentNotFound);

        return new AttachmentFile(stream, attachment.ContentType, attachment.FileName, attachment.ContentType.StartsWith("image/", StringComparison.Ordinal));
    }
}

/// <summary>The files sent in the whole system, for the admins. It shows who sent each one and when, never the text of the conversations.</summary>
public sealed record ListAttachmentsQuery(int Page = 1, int PageSize = 20) : IQuery<PagedResult<AdminAttachmentDto>>;

public sealed class ListAttachmentsQueryValidator : AbstractValidator<ListAttachmentsQuery>
{
    public ListAttachmentsQueryValidator(ILocalizer localizer)
    {
        this.AddPagingRules(localizer, x => x.Page, x => x.PageSize);
    }
}

public sealed class ListAttachmentsQueryHandler : IQueryHandler<ListAttachmentsQuery, PagedResult<AdminAttachmentDto>>
{
    private readonly IConversationRepository _conversations;

    public ListAttachmentsQueryHandler(IConversationRepository conversations)
    {
        _conversations = conversations;
    }

    public async Task<PagedResult<AdminAttachmentDto>> HandleAsync(ListAttachmentsQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _conversations.GetAttachmentsPageAsync(query.Page, query.PageSize, cancellationToken);

        return new PagedResult<AdminAttachmentDto>(items.Select(ChatMapper.ToAdminDto).ToList(), query.Page, query.PageSize, totalCount);
    }
}
