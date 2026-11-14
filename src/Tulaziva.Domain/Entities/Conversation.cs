namespace Tulaziva.Domain.Entities;

/// <summary>
/// A chat between two users about an ad: the one who wrote first (the initiator) and the owner of the ad (the recipient).
/// There is one per ad and initiator. Only those two users can read it.
/// </summary>
public class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The ad the chat is about. It is not a foreign key on purpose: when the owner deletes the ad the chat stays,
    /// with the title the ad had (<see cref="AdTitle"/>).
    /// </summary>
    public Guid AdId { get; set; }

    public string AdTitle { get; set; } = string.Empty;

    public Guid InitiatorId { get; set; }
    public User Initiator { get; set; } = null!;

    /// <summary>The owner of the ad when the chat started.</summary>
    public Guid RecipientId { get; set; }
    public User Recipient { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // The last message is copied here, so the list of chats does not have to read the messages.
    public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? LastMessageSenderId { get; set; }
    public string? LastMessagePreview { get; set; }
    public bool LastMessageHasAttachments { get; set; }

    public List<Message> Messages { get; set; } = new();

    public bool HasParticipant(Guid userId) => InitiatorId == userId || RecipientId == userId;

    public User OtherParticipant(Guid userId) => InitiatorId == userId ? Recipient : Initiator;
}
