using FluentValidation;
using FluentValidation.Results;
using Tulaziva.Application.Abstractions;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;
using Tulaziva.Application.Users;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Application.Auth;

// ---- Forgot password ----

/// <summary>
/// Sends the link to choose a new password. It answers the same whether the email has an account or not, so nobody can use it
/// to find out who is registered.
/// </summary>
public sealed record ForgotPasswordCommand(string Email) : ICommand<Unit>;

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator(ILocalizer localizer)
    {
        RuleFor(x => x.Email).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.EmailRequired))
            .EmailAddress().WithMessage(_ => localizer.Get(MessageKeys.EmailInvalid));
    }
}

public sealed class ForgotPasswordCommandHandler : ICommandHandler<ForgotPasswordCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly IAccountEmailService _accountEmails;

    public ForgotPasswordCommandHandler(IUserRepository users, IAccountEmailService accountEmails)
    {
        _users = users;
        _accountEmails = accountEmails;
    }

    public async Task<Unit> HandleAsync(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await _users.GetByEmailAsync(command.Email.Trim().ToLowerInvariant(), cancellationToken);

        if (user is { IsActive: true })
            await _accountEmails.SendPasswordResetAsync(user, cancellationToken);

        return default;
    }
}

// ---- Reset password ----

/// <summary>Chooses a new password with the token of the email. The token works once.</summary>
public sealed record ResetPasswordCommand(string Token, string NewPassword) : ICommand<Unit>;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator(ILocalizer localizer)
    {
        RuleFor(x => x.Token).NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.TokenRequired));
        this.AddPasswordRules(localizer, x => x.NewPassword);
    }
}

public sealed class ResetPasswordCommandHandler : ICommandHandler<ResetPasswordCommand, Unit>
{
    private readonly IUserTokenRepository _tokens;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILocalizer _localizer;
    private readonly TimeProvider _time;

    public ResetPasswordCommandHandler(IUserTokenRepository tokens, IPasswordHasher passwordHasher, ILocalizer localizer, TimeProvider time)
    {
        _tokens = tokens;
        _passwordHasher = passwordHasher;
        _localizer = localizer;
        _time = time;
    }

    public async Task<Unit> HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var token = await _tokens.FindValidAsync(UserTokenRules.Hash(command.Token.Trim()), TokenPurpose.PasswordReset, now, cancellationToken);

        if (token == null || !token.User.IsActive)
            throw new ValidationException(new[] { new ValidationFailure("Token", _localizer.Get(MessageKeys.TokenInvalid)) });

        token.User.PasswordHash = _passwordHasher.Hash(command.NewPassword);
        token.UsedAtUtc = now;

        // Whoever had the old password must not keep a session that renews itself.
        await _tokens.InvalidateAsync(token.UserId, TokenPurpose.RefreshToken, now, cancellationToken);

        // A reset link also proves the address belongs to the user.
        token.User.EmailVerifiedAtUtc ??= now;

        // The user and the token are saved together: they share the same unit of work.
        await _tokens.SaveChangesAsync(cancellationToken);

        return default;
    }
}

// ---- Verify email ----

/// <summary>Confirms the address of the user with the token of the email.</summary>
public sealed record VerifyEmailCommand(string Token) : ICommand<Unit>;

public sealed class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailCommandValidator(ILocalizer localizer)
    {
        RuleFor(x => x.Token).NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.TokenRequired));
    }
}

public sealed class VerifyEmailCommandHandler : ICommandHandler<VerifyEmailCommand, Unit>
{
    private readonly IUserTokenRepository _tokens;
    private readonly ILocalizer _localizer;
    private readonly TimeProvider _time;

    public VerifyEmailCommandHandler(IUserTokenRepository tokens, ILocalizer localizer, TimeProvider time)
    {
        _tokens = tokens;
        _localizer = localizer;
        _time = time;
    }

    public async Task<Unit> HandleAsync(VerifyEmailCommand command, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var token = await _tokens.FindValidAsync(UserTokenRules.Hash(command.Token.Trim()), TokenPurpose.EmailVerification, now, cancellationToken);

        if (token == null || !token.User.IsActive)
            throw new ValidationException(new[] { new ValidationFailure("Token", _localizer.Get(MessageKeys.TokenInvalid)) });

        token.User.EmailVerifiedAtUtc ??= now;
        token.UsedAtUtc = now;

        await _tokens.SaveChangesAsync(cancellationToken);

        return default;
    }
}

// ---- Send the verification again ----

/// <summary>Sends a new verification link to the signed-in user. Does nothing when the address is already verified.</summary>
public sealed record ResendVerificationCommand(Guid UserId) : ICommand<Unit>;

public sealed class ResendVerificationCommandHandler : ICommandHandler<ResendVerificationCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly IAccountEmailService _accountEmails;

    public ResendVerificationCommandHandler(IUserRepository users, IAccountEmailService accountEmails)
    {
        _users = users;
        _accountEmails = accountEmails;
    }

    public async Task<Unit> HandleAsync(ResendVerificationCommand command, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(command.UserId, cancellationToken);

        if (user == null || !user.IsActive)
            throw new NotFoundException(MessageKeys.UserNotFound);

        if (user.EmailVerifiedAtUtc == null)
            await _accountEmails.SendVerificationAsync(user, cancellationToken);

        return default;
    }
}
