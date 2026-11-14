using FluentValidation;
using Tulaziva.Application.Abstractions;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;
using Tulaziva.Application.Users;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Application.Auth;

/// <summary>Public sign-up. Always creates a regular user; admins are created by another admin.</summary>
public sealed record RegisterUserCommand(
    string Email,
    string Password,
    string DisplayName,
    string? Language = null,
    double? Latitude = null,
    double? Longitude = null,
    bool AcceptedTerms = false) : ICommand<AuthResponse>, INewUserData;

public sealed class RegisterUserCommandValidator : NewUserDataValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator(ILocalizer localizer) : base(localizer)
    {
        // The web app does not enable the button without it, but the API is the one that has to refuse.
        RuleFor(x => x.AcceptedTerms).Equal(true).WithMessage(_ => localizer.Get(MessageKeys.TermsNotAccepted));
    }
}

public sealed class RegisterUserCommandHandler : ICommandHandler<RegisterUserCommand, AuthResponse>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISessionService _sessions;
    private readonly IAccountEmailService _accountEmails;

    public RegisterUserCommandHandler(IUserRepository users, IPasswordHasher passwordHasher, ISessionService sessions, IAccountEmailService accountEmails)
    {
        _accountEmails = accountEmails;
        _users = users;
        _passwordHasher = passwordHasher;
        _sessions = sessions;
    }

    public async Task<AuthResponse> HandleAsync(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim().ToLowerInvariant();

        if (await _users.EmailExistsAsync(email, cancellationToken))
            throw new ConflictException(MessageKeys.EmailTaken);

        var user = UserFactory.Create(command, UserRole.User, _passwordHasher.Hash(command.Password));
        user.TermsAcceptedAtUtc = DateTime.UtcNow;
        user.TermsVersion = LegalRules.TermsVersion;

        await _users.AddAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        // The account works without it; the link only proves the address is the user's.
        await _accountEmails.SendVerificationAsync(user, cancellationToken);

        return await _sessions.StartAsync(user, cancellationToken);
    }
}
