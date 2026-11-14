using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Tulaziva.Application.Chat;

namespace Tulaziva.Api.Realtime;

/// <summary>
/// The live channel of the chats. Clients only listen: they send messages through the normal API endpoints, so the
/// rules (who can write where, the files that are accepted) stay in one place.
/// The server calls "MessageReceived" (a <see cref="MessageNotification"/>) and "ConversationRead" (the conversation id).
/// </summary>
[Authorize]
public sealed class ChatHub : Hub
{
    public const string Path = "/hubs/chat";
    public const string MessageReceived = "MessageReceived";
    public const string ConversationRead = "ConversationRead";
}

/// <summary>A connection belongs to the user of its token: the "sub" claim, which the API keeps with that name.</summary>
public sealed class SubClaimUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User.FindFirst("sub")?.Value;
}

/// <summary>Sends the chat events to the connections of the user. A failure is swallowed: the message is already saved.</summary>
public sealed class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<ChatHub> _hub;
    private readonly ILogger<SignalRRealtimeNotifier> _logger;

    public SignalRRealtimeNotifier(IHubContext<ChatHub> hub, ILogger<SignalRRealtimeNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public Task MessageReceivedAsync(Guid recipientId, MessageNotification notification, CancellationToken cancellationToken) =>
        SendAsync(recipientId, ChatHub.MessageReceived, notification, cancellationToken);

    public Task ConversationReadAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) =>
        SendAsync(userId, ChatHub.ConversationRead, conversationId, cancellationToken);

    private async Task SendAsync(Guid userId, string method, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await _hub.Clients.User(userId.ToString()).SendAsync(method, payload, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not send {Method} to user {UserId}", method, userId);
        }
    }
}
