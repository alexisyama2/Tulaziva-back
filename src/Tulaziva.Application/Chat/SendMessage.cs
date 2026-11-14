using FluentValidation;
using FluentValidation.Results;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Blocks;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Entities;

namespace Tulaziva.Application.Chat;

/// <summary>
/// Sends a message in a conversation of the signed-in user: text, files, or both. Each file is checked against its extension
/// and its content before anything is stored, so a message with one bad file is refused as a whole.
/// </summary>
public sealed record SendMessageCommand(Guid UserId, Guid ConversationId, string? Body, IReadOnlyList<IncomingFile> Files) : ICommand<MessageDto>
{
    public SendMessageCommand(Guid userId, Guid conversationId, string? body) : this(userId, conversationId, body, Array.Empty<IncomingFile>()) { }
}

public sealed class SendMessageCommandValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageCommandValidator(ILocalizer localizer)
    {
        RuleFor(x => x.Body)
            .MaximumLength(ChatRules.MaxMessageLength).WithMessage(_ => localizer.Get(MessageKeys.MessageTooLong, ChatRules.MaxMessageLength));

        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Body) || x.Files.Count > 0)
            .WithMessage(_ => localizer.Get(MessageKeys.MessageEmpty))
            .OverridePropertyName("Body");

        RuleFor(x => x.Files.Count)
            .LessThanOrEqualTo(ChatRules.MaxFilesPerMessage).WithMessage(_ => localizer.Get(MessageKeys.TooManyFiles, ChatRules.MaxFilesPerMessage))
            .OverridePropertyName("Files");

        RuleForEach(x => x.Files).ChildRules(file =>
        {
            file.RuleFor(f => f.FileName).Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.FileRequired))
                .Must(name => AttachmentRules.TypeOf(Path.GetExtension(name)) != null)
                .WithMessage(_ => localizer.Get(MessageKeys.AttachmentExtensionUnsupported, string.Join(", ", AttachmentRules.Extensions)));

            file.RuleFor(f => f.Length).Cascade(CascadeMode.Stop)
                .GreaterThan(0).WithMessage(_ => localizer.Get(MessageKeys.FileRequired))
                .LessThanOrEqualTo(ChatRules.MaxFileBytes).WithMessage(_ => localizer.Get(MessageKeys.FileTooLarge, ChatRules.MaxFileBytes / (1024 * 1024)));
        });
    }
}

public sealed class SendMessageCommandHandler : ICommandHandler<SendMessageCommand, MessageDto>
{
    private readonly IConversationRepository _conversations;
    private readonly IFileStorage _storage;
    private readonly ILocalizer _localizer;
    private readonly TimeProvider _time;
    private readonly IRealtimeNotifier _notifier;
    private readonly IBlockRepository _blocks;

    public SendMessageCommandHandler(IConversationRepository conversations, IFileStorage storage, ILocalizer localizer, TimeProvider time, IRealtimeNotifier notifier, IBlockRepository blocks)
    {
        _blocks = blocks;
        _notifier = notifier;
        _conversations = conversations;
        _storage = storage;
        _localizer = localizer;
        _time = time;
    }

    public async Task<MessageDto> HandleAsync(SendMessageCommand command, CancellationToken cancellationToken)
    {
        var conversation = await ChatAccess.GetForParticipantAsync(_conversations, command.ConversationId, command.UserId, cancellationToken);

        // A deleted account cannot write, and nobody can write to it.
        var other = conversation.OtherParticipant(command.UserId);
        var self = conversation.InitiatorId == command.UserId ? conversation.Initiator : conversation.Recipient;

        if (!self.IsActive || !other.IsActive)
            throw new ConflictException(MessageKeys.ConversationUnavailable);

        // The same answer whoever blocked whom: the one who was blocked is not told.
        if (await _blocks.ExistsEitherWayAsync(command.UserId, other.Id, cancellationToken))
            throw new ForbiddenException(MessageKeys.ConversationBlocked);

        // 1. Check every file before storing any.
        var prepared = new List<PreparedFile>();

        try
        {
            foreach (var file in command.Files)
                prepared.Add(await PrepareAsync(file, cancellationToken));

            // 2. Store them.
            var now = _time.GetUtcNow().UtcDateTime;
            var message = new Message
            {
                ConversationId = conversation.Id,
                SenderId = command.UserId,
                Body = string.IsNullOrWhiteSpace(command.Body) ? null : command.Body.Trim(),
                CreatedAtUtc = now
            };

            var storedPaths = new List<string>();

            try
            {
                foreach (var file in prepared)
                {
                    var path = await _storage.SaveAsync($"chat/{conversation.Id:N}", file.Content, file.Type.Extension, cancellationToken);
                    storedPaths.Add(path);

                    message.Attachments.Add(new MessageAttachment
                    {
                        MessageId = message.Id,
                        StoragePath = path,
                        FileName = file.CleanName,
                        ContentType = file.Type.Type.ContentType,
                        SizeBytes = file.Length,
                        CreatedAtUtc = now
                    });
                }

                // 3. The message and the copy of it that the list of chats shows.
                conversation.LastMessageAtUtc = now;
                conversation.LastMessageSenderId = command.UserId;
                conversation.LastMessageHasAttachments = message.Attachments.Count > 0;
                conversation.LastMessagePreview = message.Body is { } text
                    ? (text.Length > ChatRules.PreviewLength ? text[..ChatRules.PreviewLength] : text)
                    : null;

                _conversations.AddMessage(message);
                await _conversations.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // Nothing was saved, so the files that were already stored would be orphans.
                foreach (var path in storedPaths)
                    await _storage.DeleteAsync(path, CancellationToken.None);

                throw;
            }

            var dto = ChatMapper.ToDto(message);
            var sender = conversation.InitiatorId == command.UserId ? conversation.Initiator : conversation.Recipient;

            await _notifier.MessageReceivedAsync(
                conversation.OtherParticipant(command.UserId).Id,
                new MessageNotification(dto, sender.DisplayName, conversation.AdTitle),
                cancellationToken);

            return dto;
        }
        finally
        {
            foreach (var file in prepared)
                file.Dispose();
        }
    }

    private async Task<PreparedFile> PrepareAsync(IncomingFile file, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var type = AttachmentRules.TypeOf(extension)
            ?? throw Invalid("Files", MessageKeys.AttachmentExtensionUnsupported, string.Join(", ", AttachmentRules.Extensions));

        var cleanName = AttachmentRules.CleanFileName(file.FileName);

        var upload = await UploadStreams.PeekAsync(file.Content, ChatRules.HeaderLength, cancellationToken);

        if (!AttachmentRules.ContentMatches(extension, upload.Header))
        {
            upload.Dispose();
            throw Invalid("Files", MessageKeys.AttachmentContentInvalid, cleanName);
        }

        return new PreparedFile(upload, new PreparedType(type, extension), cleanName, file.Length);
    }

    private ValidationException Invalid(string property, string messageKey, params object[] args) =>
        new(new[] { new ValidationFailure(property, _localizer.Get(messageKey, args)) });

    private readonly record struct PreparedType(AttachmentType Type, string Extension);

    private sealed class PreparedFile : IDisposable
    {
        private readonly PeekedStream _upload;

        public PreparedFile(PeekedStream upload, PreparedType type, string cleanName, long length)
        {
            _upload = upload;
            Type = type;
            CleanName = cleanName;
            Length = length;
        }

        public Stream Content => _upload.Content;
        public PreparedType Type { get; }
        public string CleanName { get; }
        public long Length { get; }

        public void Dispose() => _upload.Dispose();
    }
}
