using Tulaziva.Application.Abstractions;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;

namespace Tulaziva.Application.Users;

public sealed record GetCurrentUserQuery(Guid UserId) : IQuery<UserDto>;

public sealed class GetCurrentUserQueryHandler : IQueryHandler<GetCurrentUserQuery, UserDto>
{
    private readonly IUserRepository _users;

    public GetCurrentUserQueryHandler(IUserRepository users)
    {
        _users = users;
    }

    public async Task<UserDto> HandleAsync(GetCurrentUserQuery query, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(query.UserId, cancellationToken);

        if (user == null || !user.IsActive)
            throw new NotFoundException(MessageKeys.UserNotFound);

        return UserDto.From(user);
    }
}
