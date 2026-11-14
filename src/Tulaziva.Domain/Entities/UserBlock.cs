namespace Tulaziva.Domain.Entities;

/// <summary>
/// A user (the blocker) stopped another one (the blocked) from writing to them. While it exists neither of them can start a chat
/// with the other or send a message in the chats they share. The chats and their messages are kept.
/// </summary>
public class UserBlock
{
    public Guid BlockerId { get; set; }
    public User Blocker { get; set; } = null!;

    public Guid BlockedId { get; set; }
    public User Blocked { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
