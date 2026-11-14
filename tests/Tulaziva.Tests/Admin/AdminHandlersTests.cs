using FluentAssertions;
using Tulaziva.Application.Admin;
using Tulaziva.Application.Common;
using Tulaziva.Application.Localization;
using Tulaziva.Application.Users;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Admin;

public class AdminHandlersTests
{
    private readonly FakeUserRepository _users = new();

    [Fact]
    public async Task CreateAdmin_CreatesAUserWithTheAdminRole()
    {
        var handler = new CreateAdminUserCommandHandler(_users, new FakePasswordHasher());

        var dto = await handler.HandleAsync(new CreateAdminUserCommand("Boss@Mail.com", "Password1", "Boss"), CancellationToken.None);

        dto.Role.Should().Be("Admin");
        var user = _users.Users.Should().ContainSingle().Subject;
        user.Role.Should().Be(UserRole.Admin);
        user.Email.Should().Be("boss@mail.com");
        user.PasswordHash.Should().Be("hashed:Password1");
    }

    [Fact]
    public async Task CreateAdmin_EmailAlreadyRegistered_ThrowsConflict()
    {
        _users.Users.Add(new User { Email = "boss@mail.com" });
        var handler = new CreateAdminUserCommandHandler(_users, new FakePasswordHasher());

        var act = () => handler.HandleAsync(new CreateAdminUserCommand("BOSS@mail.com", "Password1", "Boss"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task GetUsers_ReturnsTheRequestedPageAndTheTotal()
    {
        for (var i = 0; i < 5; i++)
            _users.Users.Add(new User { Email = $"user{i}@mail.com", DisplayName = $"User {i}", CreatedAtUtc = new DateTime(2026, 1, 1 + i, 0, 0, 0, DateTimeKind.Utc) });

        var handler = new GetUsersQueryHandler(_users);

        var page = await handler.HandleAsync(new GetUsersQuery(Page: 2, PageSize: 2), CancellationToken.None);

        page.TotalCount.Should().Be(5);
        page.Page.Should().Be(2);
        page.Items.Select(u => u.Email).Should().Equal("user2@mail.com", "user1@mail.com");
    }

    [Theory]
    [InlineData(0, 20, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 101, false)]
    [InlineData(1, 100, true)]
    [InlineData(3, 1, true)]
    public void GetUsersValidator_ChecksThePaging(int page, int pageSize, bool valid)
    {
        var validator = new GetUsersQueryValidator(new Localizer());

        validator.Validate(new GetUsersQuery(page, pageSize)).IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task GetCurrentUser_ReturnsTheUser_AndFailsForAnUnknownOrDisabledOne()
    {
        var ana = new User { Email = "ana@mail.com", DisplayName = "Ana" };
        var disabled = new User { Email = "off@mail.com", IsActive = false };
        _users.Users.AddRange(new[] { ana, disabled });
        var handler = new GetCurrentUserQueryHandler(_users);

        (await handler.HandleAsync(new GetCurrentUserQuery(ana.Id), CancellationToken.None)).Email.Should().Be("ana@mail.com");

        await ((Func<Task>)(() => handler.HandleAsync(new GetCurrentUserQuery(Guid.NewGuid()), CancellationToken.None)))
            .Should().ThrowAsync<NotFoundException>();
        await ((Func<Task>)(() => handler.HandleAsync(new GetCurrentUserQuery(disabled.Id), CancellationToken.None)))
            .Should().ThrowAsync<NotFoundException>();
    }
}
