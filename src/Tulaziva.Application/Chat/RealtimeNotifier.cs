namespace Tulaziva.Application.Chat;

/// <summary>What a user receives, live, when someone writes to them.</summary>
public sealed record MessageNotification(MessageDto Message, string SenderDisplayName, string AdTitle);

/// <summary>
/// Tells connected users that something happened in their chats, so the app does not have to ask again and again.
/// It is best effort (an implementation never throws, a failure must not undo a message that was saved) and only a hint: the data is still read from the API, and a user who is not connected finds it there later.
/// </summary>
public interface IRealtimeNotifier
{
    /// <summary>A new message arrived for this user.</summary>
    Task MessageReceivedAsync(Guid recipientId, MessageNotification notification, CancellationToken cancellationToken);

    /// <summary>The other participant read the messages of the conversation (so the "read" mark can change).</summary>
    Task ConversationReadAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken);
}

/// <summary>Does nothing. Used when there is no live channel (tests, or a host without it).</summary>
public sealed class NullRealtimeNotifier : IRealtimeNotifier
{
    public static readonly NullRealtimeNotifier Instance = new();

    public Task MessageReceivedAsync(Guid recipientId, MessageNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ConversationReadAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) => Task.CompletedTask;
}
