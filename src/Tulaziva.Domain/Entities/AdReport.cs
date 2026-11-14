using Tulaziva.Domain.Enums;

namespace Tulaziva.Domain.Entities;

/// <summary>
/// A complaint of a user about an ad. Like <see cref="Conversation"/>, it does not have a foreign key to the ad: if the owner deletes
/// the ad to escape the complaint, the report stays for the admins, with the title the ad had.
/// </summary>
public class AdReport
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AdId { get; set; }

    public string AdTitle { get; set; } = string.Empty;

    public Guid ReporterId { get; set; }
    public User Reporter { get; set; } = null!;

    public ReportReason Reason { get; set; }

    /// <summary>What the reporter wants to add. Optional.</summary>
    public string? Details { get; set; }

    public ReportStatus Status { get; set; } = ReportStatus.Open;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ResolvedAtUtc { get; set; }

    /// <summary>The admin who closed it.</summary>
    public Guid? ResolvedById { get; set; }
}
