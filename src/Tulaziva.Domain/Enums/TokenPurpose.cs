namespace Tulaziva.Domain.Enums;

/// <summary>What a single-use token is for. The first two and the email change are sent by email; the refresh token is kept by the web app.</summary>
public enum TokenPurpose
{
    PasswordReset = 0,
    EmailVerification = 1,

    /// <summary>Lets the web app get a new access token without asking for the password again. It is replaced each time it is used.</summary>
    RefreshToken = 2,

    /// <summary>Confirms the new address of the account. The address is in <see cref="Entities.UserToken.Payload"/>.</summary>
    EmailChange = 3
}
