using FluentValidation;
using Tulaziva.Application.Abstractions;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;
using Tulaziva.Application.Users;

namespace Tulaziva.Application.Auth;

public sealed record LoginCommand(string Email, string Password) : ICommand<AuthResponse>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator(ILocalizer localizer)
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.EmailRequired));
        RuleFor(x => x.Password).NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.PasswordRequired));
    }
}

public sealed class LoginCommandHandler : ICommandHandler<LoginCommand, AuthResponse>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISessionService _sessions;

    public LoginCommandHandler(IUserRepository users, IPasswordHasher passwordHasher, ISessionService sessions)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _sessions = sessions;
    }

    public async Task<AuthResponse> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await _users.GetByEmailAsync(command.Email.Trim().ToLowerInvariant(), cancellationToken);

        // Same answer for an unknown email, a wrong password and a disabled account, so the response never tells which one it was.
        if (user == null || !user.IsActive || !_passwordHasher.Verify(command.Password, user.PasswordHash))
            throw new UnauthenticatedException(MessageKeys.InvalidCredentials);

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _users.SaveChangesAsync(cancellationToken);

        return await _sessions.StartAsync(user, cancellationToken);
    }
}
