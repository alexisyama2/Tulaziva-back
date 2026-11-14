using FluentValidation;
using FluentValidation.Results;
using Tulaziva.Application.Abstractions;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Auth;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Application.Users;

/// <summary>
/// Removes the data an account owns when it is deleted: its ads (with their images), its favorites, its blocks, its tokens.
/// What other people also hold stays: the chats, with the user shown as deleted, and the reports.
/// </summary>
public interface IAccountDataRepository
{
    /// <summary>Marks that data for removal and returns the storage paths of the files that go with it. Saved with the unit of work of the user.</summary>
    Task<IReadOnlyList<string>> RemoveOwnedDataAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Deletes the account of the signed-in user, who must give the password. The row of the user stays, anonymized, because the chats and
/// the reports point to it; nothing that identifies the person is kept in it, and nobody can sign in to it.
/// </summary>
public sealed record DeleteAccountCommand(Guid UserId, string Password) : ICommand<Unit>;

public sealed class DeleteAccountCommandValidator : AbstractValidator<DeleteAccountCommand>
{
    public DeleteAccountCommandValidator(ILocalizer localizer)
    {
        RuleFor(x => x.Password).NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.PasswordRequired));
    }
}

public sealed class DeleteAccountCommandHandler : ICommandHandler<DeleteAccountCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAccountDataRepository _data;
    private readonly IUserTokenRepository _tokens;
    private readonly IFileStorage _storage;
    private readonly ILocalizer _localizer;
    private readonly TimeProvider _time;

    public DeleteAccountCommandHandler(
        IUserRepository users, IPasswordHasher passwordHasher, IAccountDataRepository data, IUserTokenRepository tokens,
        IFileStorage storage, ILocalizer localizer, TimeProvider time)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _data = data;
        _tokens = tokens;
        _storage = storage;
        _localizer = localizer;
        _time = time;
    }

    public async Task<Unit> HandleAsync(DeleteAccountCommand command, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(command.UserId, cancellationToken);

        if (user == null || !user.IsActive)
            throw new NotFoundException(MessageKeys.UserNotFound);

        // A 400 and not a 401: a 401 would make the web app think the session expired.
        if (!_passwordHasher.Verify(command.Password, user.PasswordHash))
            throw new ValidationException(new[] { new ValidationFailure("Password", _localizer.Get(MessageKeys.CurrentPasswordIncorrect)) });

        // Admins are created and removed by other admins; this door must not leave the system without one.
        if (user.Role == UserRole.Admin)
            throw new ConflictException(MessageKeys.AdminCannotDeleteAccount);

        var now = _time.GetUtcNow().UtcDateTime;
        var files = (await _data.RemoveOwnedDataAsync(user.Id, cancellationToken)).ToList();

        if (user.ProfilePhotoPath != null)
            files.Add(user.ProfilePhotoPath);

        foreach (var purpose in new[] { TokenPurpose.RefreshToken, TokenPurpose.PasswordReset, TokenPurpose.EmailVerification, TokenPurpose.EmailChange })
            await _tokens.InvalidateAsync(user.Id, purpose, now, cancellationToken);

        user.Email = $"deleted-{user.Id:N}@deleted.invalid";
        user.DisplayName = "Deleted user";
        user.PasswordHash = _passwordHasher.Hash(UserTokenRules.NewSecret());
        user.RegistrationLatitude = null;
        user.RegistrationLongitude = null;
        user.ProfilePhotoPath = null;
        user.ProfilePhotoContentType = null;
        user.ProfilePhotoUpdatedAtUtc = null;
        user.EmailVerifiedAtUtc = null;
        user.IsActive = false;
        user.DeletedAtUtc = now;

        await _users.SaveChangesAsync(cancellationToken);

        // After the database, like when an ad is deleted: a file that cannot be removed is an orphan, not a broken row.
        foreach (var path in files)
        {
            try
            {
                await _storage.DeleteAsync(path, cancellationToken);
            }
            catch (IOException)
            {
            }
        }

        return default;
    }
}
