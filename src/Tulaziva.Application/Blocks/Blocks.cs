using Tulaziva.Application.Abstractions;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;
using Tulaziva.Application.Users;
using Tulaziva.Domain.Entities;

namespace Tulaziva.Application.Blocks;

public interface IBlockRepository
{
    /// <summary>Whether either of the two users blocked the other.</summary>
    Task<bool> ExistsEitherWayAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken);

    Task AddAsync(UserBlock block, CancellationToken cancellationToken);

    /// <summary>Removes the block if it exists. Saved with SaveChangesAsync.</summary>
    Task RemoveAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken);

    /// <summary>The users this user blocked, with the blocked user loaded, the last blocked first.</summary>
    Task<IReadOnlyList<UserBlock>> GetByBlockerAsync(Guid blockerId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record BlockedUserDto(Guid Id, string DisplayName, string? PhotoUrl, DateTime BlockedAtUtc);

// ---- Block ----

/// <summary>Blocks a user. Blocking twice is harmless.</summary>
public sealed record BlockUserCommand(Guid UserId, Guid TargetId) : ICommand<Unit>;

public sealed class BlockUserCommandHandler : ICommandHandler<BlockUserCommand, Unit>
{
    private readonly IBlockRepository _blocks;
    private readonly IUserRepository _users;
    private readonly TimeProvider _time;

    public BlockUserCommandHandler(IBlockRepository blocks, IUserRepository users, TimeProvider time)
    {
        _blocks = blocks;
        _users = users;
        _time = time;
    }

    public async Task<Unit> HandleAsync(BlockUserCommand command, CancellationToken cancellationToken)
    {
        if (command.UserId == command.TargetId)
            throw new ConflictException(MessageKeys.CannotBlockYourself);

        var target = await _users.GetByIdAsync(command.TargetId, cancellationToken);

        if (target == null || !target.IsActive)
            throw new NotFoundException(MessageKeys.UserNotFound);

        if (await _blocks.ExistsAsync(command.UserId, target.Id, cancellationToken))
            return default;

        await _blocks.AddAsync(new UserBlock { BlockerId = command.UserId, BlockedId = target.Id, CreatedAtUtc = _time.GetUtcNow().UtcDateTime }, cancellationToken);
        await _blocks.SaveChangesAsync(cancellationToken);

        return default;
    }
}

// ---- Unblock ----

/// <summary>Removes a block. Harmless when there was none.</summary>
public sealed record UnblockUserCommand(Guid UserId, Guid TargetId) : ICommand<Unit>;

public sealed class UnblockUserCommandHandler : ICommandHandler<UnblockUserCommand, Unit>
{
    private readonly IBlockRepository _blocks;

    public UnblockUserCommandHandler(IBlockRepository blocks)
    {
        _blocks = blocks;
    }

    public async Task<Unit> HandleAsync(UnblockUserCommand command, CancellationToken cancellationToken)
    {
        await _blocks.RemoveAsync(command.UserId, command.TargetId, cancellationToken);
        await _blocks.SaveChangesAsync(cancellationToken);

        return default;
    }
}

// ---- List ----

/// <summary>The users the signed-in user blocked. It never lists who blocked them.</summary>
public sealed record GetBlockedUsersQuery(Guid UserId) : IQuery<IReadOnlyList<BlockedUserDto>>;

public sealed class GetBlockedUsersQueryHandler : IQueryHandler<GetBlockedUsersQuery, IReadOnlyList<BlockedUserDto>>
{
    private readonly IBlockRepository _blocks;

    public GetBlockedUsersQueryHandler(IBlockRepository blocks)
    {
        _blocks = blocks;
    }

    public async Task<IReadOnlyList<BlockedUserDto>> HandleAsync(GetBlockedUsersQuery query, CancellationToken cancellationToken)
    {
        var blocks = await _blocks.GetByBlockerAsync(query.UserId, cancellationToken);

        return blocks
            .Select(b => new BlockedUserDto(b.BlockedId, b.Blocked.DisplayName, UserRoutes.Photo(b.Blocked), b.CreatedAtUtc))
            .ToList();
    }
}
