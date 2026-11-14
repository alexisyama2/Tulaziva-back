using FluentValidation;
using FluentValidation.Results;
using Tulaziva.Application.Abstractions;
using Tulaziva.Application.Auth;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Application.Users;

// ---- Edit profile ----

/// <summary>
/// Changes the name, the language and the location of the signed-in user. A language or a location that is not sent stays as it was;
/// the name is always required. The email is not changed here: it identifies the account and is the one that is verified.
/// </summary>
public sealed record UpdateProfileCommand(
    Guid UserId,
    string DisplayName,
    string? Language = null,
    double? Latitude = null,
    double? Longitude = null) : ICommand<UserDto>;

public sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator(ILocalizer localizer)
    {
        RuleFor(x => x.DisplayName).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.DisplayNameRequired))
            .MaximumLength(UserRules.MaxDisplayNameLength)
            .WithMessage(_ => localizer.Get(MessageKeys.DisplayNameTooLong, UserRules.MaxDisplayNameLength));

        RuleFor(x => x.Language)
            .Must(SupportedLanguages.IsSupported)
            .When(x => !string.IsNullOrWhiteSpace(x.Language))
            .WithMessage(_ => localizer.Get(MessageKeys.LanguageUnsupported, string.Join(", ", SupportedLanguages.All)));

        this.AddLocationRules(localizer, x => x.Latitude, x => x.Longitude);
    }

}

public sealed class UpdateProfileCommandHandler : ICommandHandler<UpdateProfileCommand, UserDto>
{
    private readonly IUserRepository _users;

    public UpdateProfileCommandHandler(IUserRepository users)
    {
        _users = users;
    }

    public async Task<UserDto> HandleAsync(UpdateProfileCommand command, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(command.UserId, cancellationToken);

        if (user == null || !user.IsActive)
            throw new NotFoundException(MessageKeys.UserNotFound);

        user.DisplayName = command.DisplayName.Trim();

        if (!string.IsNullOrWhiteSpace(command.Language))
            user.PreferredLanguage = SupportedLanguages.Normalize(command.Language);

        if (command.Latitude.HasValue && command.Longitude.HasValue)
        {
            user.RegistrationLatitude = command.Latitude;
            user.RegistrationLongitude = command.Longitude;
        }

        await _users.SaveChangesAsync(cancellationToken);

        return UserDto.From(user);
    }
}

// ---- Change password ----

/// <summary>Changes the password of the signed-in user, who must know the current one.</summary>
public sealed record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword) : ICommand<Unit>;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator(ILocalizer localizer)
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.CurrentPasswordRequired));
        this.AddPasswordRules(localizer, x => x.NewPassword);
    }
}

public sealed class ChangePasswordCommandHandler : ICommandHandler<ChangePasswordCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILocalizer _localizer;
    private readonly IUserTokenRepository _tokens;
    private readonly TimeProvider _time;

    public ChangePasswordCommandHandler(IUserRepository users, IPasswordHasher passwordHasher, ILocalizer localizer, IUserTokenRepository tokens, TimeProvider time)
    {
        _tokens = tokens;
        _time = time;
        _users = users;
        _passwordHasher = passwordHasher;
        _localizer = localizer;
    }

    public async Task<Unit> HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(command.UserId, cancellationToken);

        if (user == null || !user.IsActive)
            throw new NotFoundException(MessageKeys.UserNotFound);

        // A wrong current password is a validation error (400), not a 401: a 401 would make the web app think the session expired.
        if (!_passwordHasher.Verify(command.CurrentPassword, user.PasswordHash))
            throw new ValidationException(new[] { new ValidationFailure("CurrentPassword", _localizer.Get(MessageKeys.CurrentPasswordIncorrect)) });

        user.PasswordHash = _passwordHasher.Hash(command.NewPassword);

        // The other devices have to sign in again with the new password. This one keeps its access token until it expires.
        await _tokens.InvalidateAsync(user.Id, TokenPurpose.RefreshToken, _time.GetUtcNow().UtcDateTime, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        return default;
    }
}
