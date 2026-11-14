using Tulaziva.Domain.Entities;

namespace Tulaziva.Application.Users;

public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    string PreferredLanguage,
    double? Latitude,
    double? Longitude,
    DateTime CreatedAtUtc,
    string? PhotoUrl = null,
    bool EmailVerified = false)
{
    public static UserDto From(User user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Role.ToString(),
        user.PreferredLanguage,
        user.RegistrationLatitude,
        user.RegistrationLongitude,
        user.CreatedAtUtc,
        UserRoutes.Photo(user),
        user.EmailVerifiedAtUtc.HasValue);
}

/// <summary>The access token (short lived) and, when the session can be renewed, the refresh token that gets the next one.</summary>
public sealed record AuthResponse(string Token, DateTime ExpiresAtUtc, UserDto User, string? RefreshToken = null);

public static class UserRoutes
{
    /// <summary>
    /// The URL of the profile photo of a user, or null when there is none. The photo is public: it is shown next to the ads and in the chats.
    /// The v parameter changes with each new photo, so a browser never shows the old one from its cache.
    /// </summary>
    public static string? Photo(User user) =>
        user.ProfilePhotoPath == null ? null : $"/api/users/{user.Id}/photo?v={user.ProfilePhotoUpdatedAtUtc?.Ticks ?? 0}";
}
