using FluentValidation;
using Tulaziva.Application.Abstractions;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;
using Tulaziva.Application.Users;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Application.Auth;

/// <summary>Starts a session for a user: a short-lived access token and a refresh token to get the next one without the password.</summary>
public interface ISessionService
{
    Task<AuthResponse> StartAsync(User user, CancellationToken cancellationToken);
}

public sealed class SessionService : ISessionService
{
    private readonly ITokenService _tokenService;
    private readonly IUserTokenRepository _tokens;
    private readonly TimeProvider _time;

    public SessionService(ITokenService tokenService, IUserTokenRepository tokens, TimeProvider time)
    {
        _tokenService = tokenService;
        _tokens = tokens;
        _time = time;
    }

    public async Task<AuthResponse> StartAsync(User user, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var access = _tokenService.Create(user);
        var secret = UserTokenRules.NewSecret();

        // Like the links of the emails, only the hash is stored.
        await _tokens.AddAsync(new UserToken
        {
            UserId = user.Id,
            Purpose = TokenPurpose.RefreshToken,
            TokenHash = UserTokenRules.Hash(secret),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(UserTokenRules.RefreshLifetime)
        }, cancellationToken);
        await _tokens.SaveChangesAsync(cancellationToken);

        return new AuthResponse(access.Token, access.ExpiresAtUtc, UserDto.From(user), secret);
    }
}

// ---- Refresh ----

/// <summary>Swaps a refresh token for a new access token and a new refresh token. The old refresh token stops working.</summary>
public sealed record RefreshSessionCommand(string RefreshToken) : ICommand<AuthResponse>;

public sealed class RefreshSessionCommandValidator : AbstractValidator<RefreshSessionCommand>
{
    public RefreshSessionCommandValidator(ILocalizer localizer)
    {
        RuleFor(x => x.RefreshToken).NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.TokenRequired));
    }
}

public sealed class RefreshSessionCommandHandler : ICommandHandler<RefreshSessionCommand, AuthResponse>
{
    private readonly IUserTokenRepository _tokens;
    private readonly ISessionService _sessions;
    private readonly TimeProvider _time;

    public RefreshSessionCommandHandler(IUserTokenRepository tokens, ISessionService sessions, TimeProvider time)
    {
        _tokens = tokens;
        _sessions = sessions;
        _time = time;
    }

    public async Task<AuthResponse> HandleAsync(RefreshSessionCommand command, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var token = await _tokens.FindValidAsync(UserTokenRules.Hash(command.RefreshToken.Trim()), TokenPurpose.RefreshToken, now, cancellationToken);

        // Unknown, used, expired, or the account was disabled or deleted: the same answer.
        if (token == null || !token.User.IsActive)
            throw new UnauthenticatedException(MessageKeys.SessionExpired);

        // The old one dies and the new one is saved in the same unit of work.
        token.UsedAtUtc = now;

        return await _sessions.StartAsync(token.User, cancellationToken);
    }
}

// ---- Sign out ----

/// <summary>Kills a refresh token, so the session cannot be renewed any more. Does nothing when the token is not valid.</summary>
public sealed record SignOutCommand(string RefreshToken) : ICommand<Unit>;

public sealed class SignOutCommandHandler : ICommandHandler<SignOutCommand, Unit>
{
    private readonly IUserTokenRepository _tokens;
    private readonly TimeProvider _time;

    public SignOutCommandHandler(IUserTokenRepository tokens, TimeProvider time)
    {
        _tokens = tokens;
        _time = time;
    }

    public async Task<Unit> HandleAsync(SignOutCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
            return default;

        var now = _time.GetUtcNow().UtcDateTime;
        var token = await _tokens.FindValidAsync(UserTokenRules.Hash(command.RefreshToken.Trim()), TokenPurpose.RefreshToken, now, cancellationToken);

        if (token != null)
        {
            token.UsedAtUtc = now;
            await _tokens.SaveChangesAsync(cancellationToken);
        }

        return default;
    }
}
