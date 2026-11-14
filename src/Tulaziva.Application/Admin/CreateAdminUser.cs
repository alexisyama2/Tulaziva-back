using Tulaziva.Application.Abstractions;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;
using Tulaziva.Application.Users;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Application.Admin;

/// <summary>Creates an administrator. Only an admin can send it; the API enforces that.</summary>
public sealed record CreateAdminUserCommand(
    string Email,
    string Password,
    string DisplayName,
    string? Language = null,
    double? Latitude = null,
    double? Longitude = null) : ICommand<UserDto>, INewUserData;

public sealed class CreateAdminUserCommandValidator : NewUserDataValidator<CreateAdminUserCommand>
{
    public CreateAdminUserCommandValidator(ILocalizer localizer) : base(localizer) { }
}

public sealed class CreateAdminUserCommandHandler : ICommandHandler<CreateAdminUserCommand, UserDto>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;

    public CreateAdminUserCommandHandler(IUserRepository users, IPasswordHasher passwordHasher)
    {
        _users = users;
        _passwordHasher = passwordHasher;
    }

    public async Task<UserDto> HandleAsync(CreateAdminUserCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim().ToLowerInvariant();

        if (await _users.EmailExistsAsync(email, cancellationToken))
            throw new ConflictException(MessageKeys.EmailTaken);

        var user = UserFactory.Create(command, UserRole.Admin, _passwordHasher.Hash(command.Password));

        // An admin created by another admin does not get a verification email: the creator vouches for the address.
        user.EmailVerifiedAtUtc = DateTime.UtcNow;

        await _users.AddAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        return UserDto.From(user);
    }
}
