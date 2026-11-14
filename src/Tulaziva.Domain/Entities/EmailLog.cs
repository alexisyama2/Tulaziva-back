using Tulaziva.Domain.Enums;

namespace Tulaziva.Domain.Entities;

/// <summary>
/// One email the application tried to send, and what happened to it. It never keeps the body: the links in it carry single-use tokens.
/// </summary>
public class EmailLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ToAddress { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public EmailStatus Status { get; set; } = EmailStatus.Queued;

    /// <summary>How many times sending was tried.</summary>
    public int Attempts { get; set; }

    /// <summary>Why the last attempt failed (exception types and messages, SMTP status code). Cleared when it is finally sent.</summary>
    public string? LastError { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastAttemptAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }
}
