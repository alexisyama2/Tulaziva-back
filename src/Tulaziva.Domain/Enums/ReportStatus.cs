namespace Tulaziva.Domain.Enums;

public enum ReportStatus
{
    /// <summary>Waiting for an admin.</summary>
    Open = 0,

    /// <summary>An admin agreed and took the ad down.</summary>
    Resolved = 1,

    /// <summary>An admin looked at it and left the ad as it was.</summary>
    Dismissed = 2
}
