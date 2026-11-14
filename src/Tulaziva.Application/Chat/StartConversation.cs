using Tulaziva.Application.Abstractions;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Blocks;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Application.Chat;

public sealed record StartConversationResult(ConversationDto Conversation, bool Created);

/// <summary>
/// Opens the chat of the signed-in user with the owner of an ad, or returns the one that already exists
/// (there is one per ad and user). The ad must be public: an expired or removed ad cannot be written about.
/// </summary>
public sealed record StartConversationCommand(Guid UserId, Guid AdId) : ICommand<StartConversationResult>;

public sealed class StartConversationCommandHandler : ICommandHandler<StartConversationCommand, StartConversationResult>
{
    private readonly IAdRepository _ads;
    private readonly IUserRepository _users;
    private readonly IConversationRepository _conversations;
    private readonly TimeProvider _time;
    private readonly IBlockRepository _blocks;

    public StartConversationCommandHandler(IAdRepository ads, IUserRepository users, IConversationRepository conversations, TimeProvider time, IBlockRepository blocks)
    {
        _blocks = blocks;
        _ads = ads;
        _users = users;
        _conversations = conversations;
        _time = time;
    }

    public async Task<StartConversationResult> HandleAsync(StartConversationCommand command, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var initiator = await _users.GetByIdAsync(command.UserId, cancellationToken);

        if (initiator == null || !initiator.IsActive)
            throw new NotFoundException(MessageKeys.UserNotFound);

        var ad = await AdAccess.GetAsync(_ads, command.AdId, cancellationToken);

        if (ad.Status != AdStatus.Active || ad.ExpiresAtUtc <= now)
            throw new NotFoundException(MessageKeys.AdNotFound);

        if (ad.OwnerId == initiator.Id)
            throw new ConflictException(MessageKeys.CannotMessageYourself);

        // The same answer whoever blocked whom: the one who was blocked is not told.
        if (await _blocks.ExistsEitherWayAsync(initiator.Id, ad.OwnerId, cancellationToken))
            throw new ForbiddenException(MessageKeys.ConversationBlocked);

        var existing = await _conversations.FindAsync(ad.Id, initiator.Id, cancellationToken);

        if (existing != null)
        {
            var summary = await _conversations.GetSummaryAsync(existing.Id, initiator.Id, cancellationToken)
                ?? new ConversationSummary(existing, 0);

            return new StartConversationResult(ChatMapper.ToDto(summary, initiator.Id), Created: false);
        }

        var conversation = new Conversation
        {
            AdId = ad.Id,
            AdTitle = ad.Title,
            InitiatorId = initiator.Id,
            Initiator = initiator,
            RecipientId = ad.OwnerId,
            Recipient = ad.Owner,
            CreatedAtUtc = now,
            LastMessageAtUtc = now
        };

        await _conversations.AddConversationAsync(conversation, cancellationToken);
        await _conversations.SaveChangesAsync(cancellationToken);

        return new StartConversationResult(ChatMapper.ToDto(new ConversationSummary(conversation, 0), initiator.Id), Created: true);
    }
}
