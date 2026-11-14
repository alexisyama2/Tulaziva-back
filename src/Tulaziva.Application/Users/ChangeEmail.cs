using FluentValidation;
using FluentValidation.Results;
using Tulaziva.Application.Abstractions;
using Tulaziva.Application.Auth;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Application.Users;

// ---- Ask for the change ----

/// <summary>
/// Starts the change of the address of the signed-in user. The password is required (a stolen session must not be able to take the
/// account over). Nothing changes until the link that goes to the new address is opened.
/// </summary>
public sealed record RequestEmailChangeCommand(Guid UserId, string NewEmail, string CurrentPassword) : ICommand<Unit>;

public sealed class RequestEmailChangeCommandValidator : AbstractValidator<RequestEmailChangeCommand>
{
    public RequestEmailChangeCommandValidator(ILocalizer localizer)
    {
        RuleFor(x => x.NewEmail).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.EmailRequired))
            .MaximumLength(UserRules.MaxEmailLength).WithMessage(_ => localizer.Get(MessageKeys.EmailTooLong, UserRules.MaxEmailLength))
            .EmailAddress().WithMessage(_ => localizer.Get(MessageKeys.EmailInvalid));

        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.CurrentPasswordRequired));
    }
}

public sealed class RequestEmailChangeCommandHandler : ICommandHandler<RequestEmailChangeCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAccountEmailService _accountEmails;
    private readonly ILocalizer _localizer;

    public RequestEmailChangeCommandHandler(IUserRepository users, IPasswordHasher passwordHasher, IAccountEmailService accountEmails, ILocalizer localizer)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _accountEmails = accountEmails;
        _localizer = localizer;
    }

    public async Task<Unit> HandleAsync(RequestEmailChangeCommand command, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(command.UserId, cancellationToken);

        if (user == null || !user.IsActive)
            throw new NotFoundException(MessageKeys.UserNotFound);

        // A 400 and not a 401: a 401 would make the web app think the session expired.
        if (!_passwordHasher.Verify(command.CurrentPassword, user.PasswordHash))
            throw new ValidationException(new[] { new ValidationFailure("CurrentPassword", _localizer.Get(MessageKeys.CurrentPasswordIncorrect)) });

        var newEmail = command.NewEmail.Trim().ToLowerInvariant();

        if (newEmail == user.Email)
            throw new ValidationException(new[] { new ValidationFailure("NewEmail", _localizer.Get(MessageKeys.EmailUnchanged)) });

        if (await _users.EmailExistsAsync(newEmail, cancellationToken))
            throw new ConflictException(MessageKeys.EmailTaken);

        await _accountEmails.SendEmailChangeAsync(user, newEmail, cancellationToken);

        return default;
    }
}

// ---- Confirm it ----

/// <summary>Switches the account to the new address with the token of the email. The new address counts as verified.</summary>
public sealed record ConfirmEmailChangeCommand(string Token) : ICommand<Unit>;

public sealed class ConfirmEmailChangeCommandValidator : AbstractValidator<ConfirmEmailChangeCommand>
{
    public ConfirmEmailChangeCommandValidator(ILocalizer localizer)
    {
        RuleFor(x => x.Token).NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.TokenRequired));
    }
}

public sealed class ConfirmEmailChangeCommandHandler : ICommandHandler<ConfirmEmailChangeCommand, Unit>
{
    private readonly IUserTokenRepository _tokens;
    private readonly IUserRepository _users;
    private readonly IAccountEmailService _accountEmails;
    private readonly ILocalizer _localizer;
    private readonly TimeProvider _time;

    public ConfirmEmailChangeCommandHandler(
        IUserTokenRepository tokens, IUserRepository users, IAccountEmailService accountEmails, ILocalizer localizer, TimeProvider time)
    {
        _tokens = tokens;
        _users = users;
        _accountEmails = accountEmails;
        _localizer = localizer;
        _time = time;
    }

    public async Task<Unit> HandleAsync(ConfirmEmailChangeCommand command, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var token = await _tokens.FindValidAsync(UserTokenRules.Hash(command.Token.Trim()), TokenPurpose.EmailChange, now, cancellationToken);

        if (token == null || !token.User.IsActive || string.IsNullOrWhiteSpace(token.Payload))
            throw new ValidationException(new[] { new ValidationFailure("Token", _localizer.Get(MessageKeys.TokenInvalid)) });

        var newEmail = token.Payload;

        // Somebody may have registered it since the link was sent.
        if (await _users.EmailExistsAsync(newEmail, cancellationToken))
            throw new ConflictException(MessageKeys.EmailTaken);

        var user = token.User;
        var oldEmail = user.Email;

        user.Email = newEmail;
        user.EmailVerifiedAtUtc = now;
        token.UsedAtUtc = now;

        await _tokens.SaveChangesAsync(cancellationToken);

        await _accountEmails.SendEmailChangedNoticeAsync(user, oldEmail, cancellationToken);

        return default;
    }
}
