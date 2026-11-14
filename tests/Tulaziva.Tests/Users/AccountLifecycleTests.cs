using FluentAssertions;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Auth;
using Tulaziva.Application.Common;
using Tulaziva.Application.Users;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Users;

/// <summary>The cases of the sessions, account deletion and featuring that the routes cannot reach: time and configuration.</summary>
public class AccountLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly MutableTimeProvider _time = new(Now);
    private readonly FakeUserRepository _users = new();
    private readonly FakeUserTokenRepository _tokens = new();
    private readonly User _ana = new() { Email = "ana@mail.com", DisplayName = "Ana", PasswordHash = "hashed:Password1", ProfilePhotoPath = "profiles/ana.jpg" };

    public AccountLifecycleTests()
    {
        _users.Users.Add(_ana);
    }

    // ---- Sessions ----

    private async Task<AuthResponse> StartSession() =>
        await TestSessions.Create(_tokens, _time).StartAsync(_ana, CancellationToken.None);

    // The real repository loads the user with the token; the fake does not, so it is linked here.
    private RefreshSessionCommandHandler RefreshHandler()
    {
        foreach (var token in _tokens.Tokens)
            token.User = _ana;

        return new(_tokens, TestSessions.Create(_tokens, _time), _time);
    }

    [Fact]
    public async Task Refresh_AfterThirtyDays_IsUnauthenticated()
    {
        var session = await StartSession();
        _time.Advance(UserTokenRules.RefreshLifetime + TimeSpan.FromMinutes(1));

        var act = () => RefreshHandler().HandleAsync(new RefreshSessionCommand(session.RefreshToken!), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthenticatedException>();
    }

    [Fact]
    public async Task Refresh_JustBeforeItExpires_Works_AndStartsANewPeriod()
    {
        var session = await StartSession();
        _time.Advance(UserTokenRules.RefreshLifetime - TimeSpan.FromMinutes(1));

        var renewed = await RefreshHandler().HandleAsync(new RefreshSessionCommand(session.RefreshToken!), CancellationToken.None);
        _time.Advance(TimeSpan.FromDays(29));

        var again = await RefreshHandler().HandleAsync(new RefreshSessionCommand(renewed.RefreshToken!), CancellationToken.None);

        again.RefreshToken.Should().NotBe(renewed.RefreshToken);
    }

    [Fact]
    public async Task Refresh_OfADisabledAccount_IsUnauthenticated()
    {
        var session = await StartSession();
        _ana.IsActive = false;

        var act = () => RefreshHandler().HandleAsync(new RefreshSessionCommand(session.RefreshToken!), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthenticatedException>();
    }

    [Fact]
    public async Task TheRefreshTokenIsStoredOnlyAsAHash()
    {
        var session = await StartSession();

        var stored = _tokens.Tokens.Should().ContainSingle().Subject;
        stored.TokenHash.Should().Be(UserTokenRules.Hash(session.RefreshToken!)).And.NotBe(session.RefreshToken);
        stored.Purpose.Should().Be(TokenPurpose.RefreshToken);
    }

    // ---- Delete account ----

    private (DeleteAccountCommandHandler Handler, FakeFileStorage Storage, FakeAccountDataRepository Data) DeleteHandler()
    {
        var storage = new FakeFileStorage();
        var data = new FakeAccountDataRepository();

        return (new DeleteAccountCommandHandler(_users, new FakePasswordHasher(), data, _tokens, storage, new Tulaziva.Application.Localization.Localizer(), _time), storage, data);
    }

    [Fact]
    public async Task Delete_AnonymizesTheUser_AndRemovesItsData()
    {
        var (handler, _, data) = DeleteHandler();
        data.Files.Add("ads/one.jpg");
        var session = await StartSession();

        await handler.HandleAsync(new DeleteAccountCommand(_ana.Id, "Password1"), CancellationToken.None);

        data.Removed.Should().Equal(_ana.Id);
        _ana.IsActive.Should().BeFalse();
        _ana.DeletedAtUtc.Should().Be(Now.UtcDateTime);
        _ana.Email.Should().EndWith("@deleted.invalid").And.NotContain("ana@");
        _ana.DisplayName.Should().NotContain("Ana");
        _ana.ProfilePhotoPath.Should().BeNull();
        _tokens.Tokens.Should().OnlyContain(t => t.UsedAtUtc != null);
        session.RefreshToken.Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_WithAWrongPassword_ChangesNothing()
    {
        var (handler, _, data) = DeleteHandler();

        var act = () => handler.HandleAsync(new DeleteAccountCommand(_ana.Id, "WrongPass1"), CancellationToken.None);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        data.Removed.Should().BeEmpty();
        _ana.IsActive.Should().BeTrue();
        _ana.Email.Should().Be("ana@mail.com");
    }

    // ---- Featuring ----

    private readonly FakeAdRepository _ads = new();

    private Ad NewAd(User owner, int expiresInDays = 10, AdStatus status = AdStatus.Active)
    {
        var ad = new Ad { OwnerId = owner.Id, Owner = owner, CategoryId = 1, Title = "Bici", Status = status, ExpiresAtUtc = Now.UtcDateTime.AddDays(expiresInDays) };
        _ads.Ads.Add(ad);

        return ad;
    }

    [Fact]
    public async Task Feature_WhenOwnersCannot_IsForbidden_ForTheOwner_ButNotForAnAdmin()
    {
        var ad = NewAd(_ana);
        var handler = new FeatureAdCommandHandler(_ads, new FakeFeaturingPolicy { OwnersCanFeature = false }, _time);

        var owner = () => handler.HandleAsync(new FeatureAdCommand(_ana.Id, false, ad.Id, 7), CancellationToken.None);
        await owner.Should().ThrowAsync<ForbiddenException>();

        var admin = await handler.HandleAsync(new FeatureAdCommand(Guid.NewGuid(), true, ad.Id, 7), CancellationToken.None);
        admin.FeaturedUntilUtc.Should().Be(Now.UtcDateTime.AddDays(7));
    }

    [Fact]
    public async Task Feature_AnExpiredOrRemovedAd_IsAConflict()
    {
        var handler = new FeatureAdCommandHandler(_ads, new FakeFeaturingPolicy(), _time);

        var expired = NewAd(_ana, expiresInDays: -1);
        var removed = NewAd(_ana, status: AdStatus.Removed);

        foreach (var ad in new[] { expired, removed })
        {
            var act = () => handler.HandleAsync(new FeatureAdCommand(_ana.Id, false, ad.Id, 3), CancellationToken.None);
            await act.Should().ThrowAsync<ConflictException>();
        }
    }

    [Fact]
    public async Task Feature_AfterTheLastPeriodEnded_CountsFromNow_NotFromTheOldEnd()
    {
        var ad = NewAd(_ana, expiresInDays: 100);
        ad.FeaturedUntilUtc = Now.UtcDateTime.AddDays(-5);
        var handler = new FeatureAdCommandHandler(_ads, new FakeFeaturingPolicy(), _time);

        var featured = await handler.HandleAsync(new FeatureAdCommand(_ana.Id, false, ad.Id, 3), CancellationToken.None);

        featured.FeaturedUntilUtc.Should().Be(Now.UtcDateTime.AddDays(3));
    }
}
