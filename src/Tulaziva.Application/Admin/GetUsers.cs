using FluentValidation;
using Tulaziva.Application.Abstractions;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;
using Tulaziva.Application.Users;

namespace Tulaziva.Application.Admin;

public sealed record GetUsersQuery(int Page = 1, int PageSize = 20) : IQuery<PagedResult<UserDto>>;

public sealed class GetUsersQueryValidator : AbstractValidator<GetUsersQuery>
{
    public const int MaxPageSize = 100;

    public GetUsersQueryValidator(ILocalizer localizer)
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage(_ => localizer.Get(MessageKeys.PageInvalid));
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize).WithMessage(_ => localizer.Get(MessageKeys.PageSizeInvalid, MaxPageSize));
    }
}

public sealed class GetUsersQueryHandler : IQueryHandler<GetUsersQuery, PagedResult<UserDto>>
{
    private readonly IUserRepository _users;

    public GetUsersQueryHandler(IUserRepository users)
    {
        _users = users;
    }

    public async Task<PagedResult<UserDto>> HandleAsync(GetUsersQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _users.GetPageAsync(query.Page, query.PageSize, cancellationToken);

        return new PagedResult<UserDto>(items.Select(UserDto.From).ToList(), query.Page, query.PageSize, totalCount);
    }
}
