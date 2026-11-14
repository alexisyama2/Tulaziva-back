using System.Security.Cryptography;
using System.Text;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Application.Auth;

/// <summary>An email: plain text always, and an HTML version for the clients that can show it.</summary>
public sealed record EmailMessage(string To, string Subject, string Body, string? HtmlBody = null);

/// <summary>
/// Sends an email. It is best effort: an implementation logs its own failures and does not throw, so a mail server that is down
/// never turns a sign-up or a "forgot my password" request into an error.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>The links of the emails. They point to the web app, which then calls the API with the token.</summary>
public interface IAppLinks
{
    string PasswordReset(string token);

    string VerifyEmail(string token);

    string ConfirmEmailChange(string token);
}

public interface IUserTokenRepository
{
    Task AddAsync(UserToken token, CancellationToken cancellationToken);

    /// <summary>The token with that hash and purpose, with its user, when it was not used and has not expired. Tracked.</summary>
    Task<UserToken?> FindValidAsync(string tokenHash, TokenPurpose purpose, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Kills the unused tokens of the user for that purpose (a new request replaces the old links). Saved with SaveChangesAsync.</summary>
    Task InvalidateAsync(Guid userId, TokenPurpose purpose, DateTime nowUtc, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public static class UserTokenRules
{
    public static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan EmailVerificationLifetime = TimeSpan.FromDays(2);
    public static readonly TimeSpan EmailChangeLifetime = TimeSpan.FromDays(2);

    /// <summary>How long a session can be renewed without the password. Each use of the refresh token starts a new period.</summary>
    public static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);

    /// <summary>A random secret for a link: 32 bytes, URL safe.</summary>
    public static string NewSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>What is stored instead of the secret.</summary>
    public static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}

/// <summary>Creates the single-use tokens of an account and sends them by email, in the language of the user.</summary>
public interface IAccountEmailService
{
    Task SendVerificationAsync(User user, CancellationToken cancellationToken);

    Task SendPasswordResetAsync(User user, CancellationToken cancellationToken);

    /// <summary>Sends the link that confirms the new address, to the new address. The address of the account does not change until the link is used.</summary>
    Task SendEmailChangeAsync(User user, string newEmail, CancellationToken cancellationToken);

    /// <summary>Tells the old address that the account now uses another one, so a stolen account cannot change it unnoticed.</summary>
    Task SendEmailChangedNoticeAsync(User user, string oldEmail, CancellationToken cancellationToken);
}

public sealed class AccountEmailService : IAccountEmailService
{
    private readonly IUserTokenRepository _tokens;
    private readonly IEmailSender _email;
    private readonly IAppLinks _links;
    private readonly TimeProvider _time;

    public AccountEmailService(IUserTokenRepository tokens, IEmailSender email, IAppLinks links, TimeProvider time)
    {
        _tokens = tokens;
        _email = email;
        _links = links;
        _time = time;
    }

    public async Task SendVerificationAsync(User user, CancellationToken cancellationToken)
    {
        var secret = await CreateTokenAsync(user, TokenPurpose.EmailVerification, UserTokenRules.EmailVerificationLifetime, cancellationToken);
        var email = EmailTemplates.VerifyEmail(user.PreferredLanguage, user.DisplayName, _links.VerifyEmail(secret));

        await _email.SendAsync(email.To(user.Email), cancellationToken);
    }

    public async Task SendPasswordResetAsync(User user, CancellationToken cancellationToken)
    {
        var secret = await CreateTokenAsync(user, TokenPurpose.PasswordReset, UserTokenRules.PasswordResetLifetime, cancellationToken);
        var email = EmailTemplates.PasswordReset(user.PreferredLanguage, user.DisplayName, _links.PasswordReset(secret));

        await _email.SendAsync(email.To(user.Email), cancellationToken);
    }

    public async Task SendEmailChangeAsync(User user, string newEmail, CancellationToken cancellationToken)
    {
        var secret = await CreateTokenAsync(user, TokenPurpose.EmailChange, UserTokenRules.EmailChangeLifetime, cancellationToken, payload: newEmail);
        var email = EmailTemplates.ConfirmEmailChange(user.PreferredLanguage, user.DisplayName, _links.ConfirmEmailChange(secret));

        await _email.SendAsync(email.To(newEmail), cancellationToken);
    }

    public async Task SendEmailChangedNoticeAsync(User user, string oldEmail, CancellationToken cancellationToken)
    {
        var email = EmailTemplates.EmailChanged(user.PreferredLanguage, user.DisplayName, user.Email);

        await _email.SendAsync(email.To(oldEmail), cancellationToken);
    }

    private async Task<string> CreateTokenAsync(User user, TokenPurpose purpose, TimeSpan lifetime, CancellationToken cancellationToken, string? payload = null)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var secret = UserTokenRules.NewSecret();

        await _tokens.InvalidateAsync(user.Id, purpose, now, cancellationToken);
        await _tokens.AddAsync(new UserToken
        {
            UserId = user.Id,
            Purpose = purpose,
            TokenHash = UserTokenRules.Hash(secret),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(lifetime),
            Payload = payload
        }, cancellationToken);
        await _tokens.SaveChangesAsync(cancellationToken);

        return secret;
    }
}

/// <summary>An email before it has a recipient: subject, text, and the label of the button that holds its link (empty when it has none).</summary>
internal sealed record RenderedEmail(string Subject, string Text, string ButtonLabel)
{
    private const string Brand = "Tulaziva";

    public EmailMessage To(string address) =>
        new(address, Subject, Text, EmailHtml.FromText(Text, ButtonLabel, Brand));
}

/// <summary>The text of the emails. Not in the request messages because the language is the one of the user, not the one of the request.</summary>
internal static class EmailTemplates
{
    private static bool English(string language) => SupportedLanguages.Normalize(language) == "en";

    public static RenderedEmail PasswordReset(string language, string name, string link) =>
        English(language)
            ? new("Reset your Tulaziva password",
                $"Hi {name},\n\nSomeone asked to reset the password of your account. To choose a new one open this link (valid for 1 hour):\n\n{link}\n\nIf it was not you, ignore this email: your password does not change.\n\nTulaziva",
                "Reset password")
            : new("Restablece tu contraseña de Tulaziva",
                $"Hola {name},\n\nAlguien pidió restablecer la contraseña de tu cuenta. Para elegir una nueva abre este enlace (vale 1 hora):\n\n{link}\n\nSi no fuiste tú, ignora este correo: tu contraseña no cambia.\n\nTulaziva",
                "Restablecer contraseña");

    public static RenderedEmail ConfirmEmailChange(string language, string name, string link) =>
        English(language)
            ? new("Confirm your new Tulaziva email",
                $"Hi {name},\n\nYou asked to use this address for your Tulaziva account. Confirm it by opening this link (valid for 2 days):\n\n{link}\n\nIf it was not you, ignore this email: the account keeps its current address.\n\nTulaziva",
                "Confirm new email")
            : new("Confirma tu nuevo correo de Tulaziva",
                $"Hola {name},\n\nPediste usar este correo en tu cuenta de Tulaziva. Confírmalo abriendo este enlace (vale 2 días):\n\n{link}\n\nSi no fuiste tú, ignora este correo: la cuenta conserva su correo actual.\n\nTulaziva",
                "Confirmar nuevo correo");

    public static RenderedEmail EmailChanged(string language, string name, string newEmail) =>
        English(language)
            ? new("The email of your Tulaziva account changed",
                $"Hi {name},\n\nThe email of your account is now {newEmail}. If it was not you, reset your password right away.\n\nTulaziva",
                string.Empty)
            : new("Cambió el correo de tu cuenta de Tulaziva",
                $"Hola {name},\n\nEl correo de tu cuenta ahora es {newEmail}. Si no fuiste tú, restablece tu contraseña de inmediato.\n\nTulaziva",
                string.Empty);

    public static RenderedEmail VerifyEmail(string language, string name, string link) =>
        English(language)
            ? new("Verify your Tulaziva email",
                $"Hi {name},\n\nWelcome to Tulaziva! Confirm that this address is yours by opening this link (valid for 2 days):\n\n{link}\n\nIf you did not create this account, ignore this email.\n\nTulaziva",
                "Verify my email")
            : new("Verifica tu correo de Tulaziva",
                $"Hola {name},\n\n¡Bienvenido a Tulaziva! Confirma que este correo es tuyo abriendo este enlace (vale 2 días):\n\n{link}\n\nSi no creaste esta cuenta, ignora este correo.\n\nTulaziva",
                "Verificar mi correo");
}
