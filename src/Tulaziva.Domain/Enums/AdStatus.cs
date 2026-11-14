namespace Tulaziva.Domain.Enums;

public enum AdStatus
{
    /// <summary>Published. Visible to everyone until it expires.</summary>
    Active = 0,

    /// <summary>Taken down by an admin. Only the owner and the admins can still see it.</summary>
    Removed = 1
}
