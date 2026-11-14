using FluentValidation;
using FluentValidation.Results;
using Tulaziva.Application.Abstractions;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Chat;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;

namespace Tulaziva.Application.Users;

public static class ProfilePhotoRules
{
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>The photo of a profile follows the same rule as the images of the chat: only .jpg, .jpeg and .png.</summary>
    public static IReadOnlyList<string> Extensions => AttachmentRules.Extensions;
}

public sealed record ProfilePhotoFile(Stream Content, string ContentType);

// ---- Upload ----

/// <summary>
/// Sets the profile photo of the signed-in user, replacing the one it had. Only .jpg, .jpeg and .png; the extension decides, and the
/// first bytes must be those of that image type too, so a program renamed to .png is refused. Length is what the server measured.
/// </summary>
public sealed record SetProfilePhotoCommand(Guid UserId, string FileName, long Length, Stream Content) : ICommand<UserDto>;

public sealed class SetProfilePhotoCommandValidator : AbstractValidator<SetProfilePhotoCommand>
{
    public SetProfilePhotoCommandValidator(ILocalizer localizer)
    {
        RuleFor(x => x.FileName).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.FileRequired))
            .Must(name => AttachmentRules.TypeOf(Path.GetExtension(name)) != null)
            .WithMessage(_ => localizer.Get(MessageKeys.AttachmentExtensionUnsupported, string.Join(", ", ProfilePhotoRules.Extensions)));

        RuleFor(x => x.Length).Cascade(CascadeMode.Stop)
            .GreaterThan(0).WithMessage(_ => localizer.Get(MessageKeys.FileRequired))
            .LessThanOrEqualTo(ProfilePhotoRules.MaxBytes).WithMessage(_ => localizer.Get(MessageKeys.FileTooLarge, ProfilePhotoRules.MaxBytes / (1024 * 1024)));
    }
}

public sealed class SetProfilePhotoCommandHandler : ICommandHandler<SetProfilePhotoCommand, UserDto>
{
    private readonly IUserRepository _users;
    private readonly IFileStorage _storage;
    private readonly ILocalizer _localizer;
    private readonly TimeProvider _time;

    public SetProfilePhotoCommandHandler(IUserRepository users, IFileStorage storage, ILocalizer localizer, TimeProvider time)
    {
        _users = users;
        _storage = storage;
        _localizer = localizer;
        _time = time;
    }

    public async Task<UserDto> HandleAsync(SetProfilePhotoCommand command, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(command.UserId, cancellationToken);

        if (user == null || !user.IsActive)
            throw new NotFoundException(MessageKeys.UserNotFound);

        var extension = Path.GetExtension(command.FileName).ToLowerInvariant();
        var type = AttachmentRules.TypeOf(extension)
            ?? throw Invalid(MessageKeys.AttachmentExtensionUnsupported, string.Join(", ", ProfilePhotoRules.Extensions));

        using var upload = await UploadStreams.PeekAsync(command.Content, ChatRules.HeaderLength, cancellationToken);

        if (!AttachmentRules.ContentMatches(extension, upload.Header))
            throw Invalid(MessageKeys.AttachmentContentInvalid, AttachmentRules.CleanFileName(command.FileName));

        // The stored extension comes from the checked type, so a .jpeg is kept as .jpg and the name never carries anything the client chose.
        var storedExtension = type.ContentType == "image/png" ? ".png" : ".jpg";
        var newPath = await _storage.SaveAsync($"profiles/{user.Id:N}", upload.Content, storedExtension, cancellationToken);

        var oldPath = user.ProfilePhotoPath;
        user.ProfilePhotoPath = newPath;
        user.ProfilePhotoContentType = type.ContentType;
        user.ProfilePhotoUpdatedAtUtc = _time.GetUtcNow().UtcDateTime;

        try
        {
            await _users.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _storage.DeleteAsync(newPath, CancellationToken.None);
            throw;
        }

        // After the database: a file that cannot be removed is only an orphan, while the other order could leave a user with a broken photo.
        if (oldPath != null)
            await TryDeleteAsync(oldPath, cancellationToken);

        return UserDto.From(user);
    }

    private async Task TryDeleteAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await _storage.DeleteAsync(path, cancellationToken);
        }
        catch (IOException)
        {
        }
    }

    private ValidationException Invalid(string messageKey, params object[] args) =>
        new(new[] { new ValidationFailure("File", _localizer.Get(messageKey, args)) });
}

// ---- Remove ----

/// <summary>Removes the profile photo of the signed-in user. Doing it when there is none is harmless.</summary>
public sealed record RemoveProfilePhotoCommand(Guid UserId) : ICommand<UserDto>;

public sealed class RemoveProfilePhotoCommandHandler : ICommandHandler<RemoveProfilePhotoCommand, UserDto>
{
    private readonly IUserRepository _users;
    private readonly IFileStorage _storage;

    public RemoveProfilePhotoCommandHandler(IUserRepository users, IFileStorage storage)
    {
        _users = users;
        _storage = storage;
    }

    public async Task<UserDto> HandleAsync(RemoveProfilePhotoCommand command, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(command.UserId, cancellationToken);

        if (user == null || !user.IsActive)
            throw new NotFoundException(MessageKeys.UserNotFound);

        var path = user.ProfilePhotoPath;

        if (path == null)
            return UserDto.From(user);

        user.ProfilePhotoPath = null;
        user.ProfilePhotoContentType = null;
        user.ProfilePhotoUpdatedAtUtc = null;
        await _users.SaveChangesAsync(cancellationToken);

        try
        {
            await _storage.DeleteAsync(path, cancellationToken);
        }
        catch (IOException)
        {
            // Only an orphan file is left; the photo is already gone from the profile.
        }

        return UserDto.From(user);
    }
}

// ---- Download ----

/// <summary>The profile photo of a user. It is public, like the name that goes with it, but a disabled account has none.</summary>
public sealed record GetProfilePhotoQuery(Guid UserId) : IQuery<ProfilePhotoFile>;

public sealed class GetProfilePhotoQueryHandler : IQueryHandler<GetProfilePhotoQuery, ProfilePhotoFile>
{
    private readonly IUserRepository _users;
    private readonly IFileStorage _storage;

    public GetProfilePhotoQueryHandler(IUserRepository users, IFileStorage storage)
    {
        _users = users;
        _storage = storage;
    }

    public async Task<ProfilePhotoFile> HandleAsync(GetProfilePhotoQuery query, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(query.UserId, cancellationToken);

        if (user == null || !user.IsActive || user.ProfilePhotoPath == null || user.ProfilePhotoContentType == null)
            throw new NotFoundException(MessageKeys.PhotoNotFound);

        var stream = await _storage.OpenReadAsync(user.ProfilePhotoPath, cancellationToken)
            ?? throw new NotFoundException(MessageKeys.PhotoNotFound);

        return new ProfilePhotoFile(stream, user.ProfilePhotoContentType);
    }
}
