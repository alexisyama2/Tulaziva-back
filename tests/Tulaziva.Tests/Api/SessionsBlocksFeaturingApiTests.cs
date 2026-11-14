using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Blocks;
using Tulaziva.Application.Chat;
using Tulaziva.Application.Common;
using Tulaziva.Application.Users;

namespace Tulaziva.Tests.Api;

/// <summary>
/// The third round of features, through the real routes: sessions that renew, changing the address, deleting the account,
/// blocking users and featuring ads.
/// </summary>
public class SessionsBlocksFeaturingApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public SessionsBlocksFeaturingApiTests(ApiFactory factory)
    {
        _factory = factory;
        // These tests send many emails from one address; the limit has its own tests.
        _client = factory
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:AccountEmailsPermitLimit"] = "1000" })))
            .CreateClient();
    }

    // ---- Helpers ----

    private sealed record Session(string Token, string RefreshToken, UserDto User, string Password);

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, string? token = null, object? body = null, string? language = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (language != null)
            request.Headers.AcceptLanguage.ParseAdd(language);

        if (body != null)
            request.Content = JsonContent.Create(body);

        return _client.SendAsync(request);
    }

    private async Task<Session> Register(string name)
    {
        var email = $"{name}-{Guid.NewGuid():N}@mail.com".ToLowerInvariant();
        var response = await Send(HttpMethod.Post, "/api/auth/register", body: new { email, password = "Password1", acceptedTerms = true, displayName = name });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;

        return new Session(auth.Token, auth.RefreshToken!, auth.User, "Password1");
    }

    private async Task<string> AdminToken()
    {
        var response = await Send(HttpMethod.Post, "/api/auth/login", body: new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!.Token;
    }

    private async Task<AdDto> CreateAd(string token, string title = "Bicicleta de montana")
    {
        var response = await Send(HttpMethod.Post, "/api/ads", token,
            new { categoryId = 1, title, description = "Casi nueva", price = 1500, currency = "MXN" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AdDto>())!;
    }

    private async Task<string[]> SearchTitles(string text) =>
        (await (await Send(HttpMethod.Get, $"/api/ads?text={text}&pageSize=50")).Content.ReadFromJsonAsync<PagedResult<AdSummaryDto>>())!
            .Items.Select(a => a.Title).ToArray();

    // ---- Sessions that renew ----

    [Fact]
    public async Task SigningUpAndIn_ReturnAARefreshToken()
    {
        var ana = await Register("ana");

        ana.RefreshToken.Should().NotBeNullOrWhiteSpace();

        var login = await Send(HttpMethod.Post, "/api/auth/login", body: new { email = ana.User.Email, password = ana.Password });
        (await login.Content.ReadFromJsonAsync<AuthResponse>())!.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Refresh_GivesANewSession_AndTheOldRefreshTokenStopsWorking()
    {
        var ana = await Register("ana");

        var refreshed = await Send(HttpMethod.Post, "/api/auth/refresh", body: new { refreshToken = ana.RefreshToken });

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await refreshed.Content.ReadFromJsonAsync<AuthResponse>())!;
        auth.RefreshToken.Should().NotBe(ana.RefreshToken);
        auth.User.Id.Should().Be(ana.User.Id);
        (await Send(HttpMethod.Get, "/api/users/me", auth.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var reused = await Send(HttpMethod.Post, "/api/auth/refresh", body: new { refreshToken = ana.RefreshToken });
        reused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The new one keeps working: each use hands out the next.
        (await Send(HttpMethod.Post, "/api/auth/refresh", body: new { refreshToken = auth.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_WithAnInventedToken_IsUnauthorizedInTheLanguageOfTheRequest()
    {
        var response = await Send(HttpMethod.Post, "/api/auth/refresh", body: new { refreshToken = "invented" }, language: "en");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("session has expired");
    }

    [Fact]
    public async Task Refresh_WithoutAToken_IsABadRequest()
    {
        (await Send(HttpMethod.Post, "/api/auth/refresh", body: new { refreshToken = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Logout_KillsTheRefreshToken()
    {
        var ana = await Register("ana");

        (await Send(HttpMethod.Post, "/api/auth/logout", body: new { refreshToken = ana.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Send(HttpMethod.Post, "/api/auth/refresh", body: new { refreshToken = ana.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Signing out with a token that is already dead is not an error.
        (await Send(HttpMethod.Post, "/api/auth/logout", body: new { refreshToken = ana.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ChangingThePassword_KillsTheRefreshTokens()
    {
        var ana = await Register("ana");

        var change = await Send(HttpMethod.Post, "/api/users/me/password", ana.Token, new { currentPassword = ana.Password, newPassword = "Another123" });
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Send(HttpMethod.Post, "/api/auth/refresh", body: new { refreshToken = ana.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ResettingThePassword_KillsTheRefreshTokens()
    {
        var ana = await Register("ana");

        (await Send(HttpMethod.Post, "/api/auth/forgot-password", body: new { email = ana.User.Email })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var token = _factory.Emails.LastTokenFor(ana.User.Email);
        (await Send(HttpMethod.Post, "/api/auth/reset-password", body: new { token, newPassword = "Another123" })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Send(HttpMethod.Post, "/api/auth/refresh", body: new { refreshToken = ana.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- Change the address ----

    [Fact]
    public async Task ChangeEmail_SendsALinkToTheNewAddress_AndOnlyTheLinkSwitchesTheAccount()
    {
        var ana = await Register("ana");
        var newEmail = $"new-{Guid.NewGuid():N}@mail.com";

        var ask = await Send(HttpMethod.Post, "/api/users/me/email", ana.Token, new { newEmail, currentPassword = ana.Password });
        ask.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Nothing changed yet.
        (await (await Send(HttpMethod.Get, "/api/users/me", ana.Token)).Content.ReadFromJsonAsync<UserDto>())!.Email.Should().Be(ana.User.Email);

        var token = _factory.Emails.LastTokenFor(newEmail);
        (await Send(HttpMethod.Post, "/api/auth/confirm-email-change", body: new { token })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var me = (await (await Send(HttpMethod.Get, "/api/users/me", ana.Token)).Content.ReadFromJsonAsync<UserDto>())!;
        me.Email.Should().Be(newEmail);
        me.EmailVerified.Should().BeTrue();

        // The new address signs in, the old one does not, and the old one is told.
        (await Send(HttpMethod.Post, "/api/auth/login", body: new { email = newEmail, password = ana.Password })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(HttpMethod.Post, "/api/auth/login", body: new { email = ana.User.Email, password = ana.Password })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.Emails.Sent.Should().Contain(m => m.To == ana.User.Email && m.Body.Contains(newEmail));

        // The link works once.
        (await Send(HttpMethod.Post, "/api/auth/confirm-email-change", body: new { token })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangeEmail_WithAWrongPassword_IsABadRequest_NotA401()
    {
        var ana = await Register("ana");

        var ask = await Send(HttpMethod.Post, "/api/users/me/email", ana.Token, new { newEmail = "x@mail.com", currentPassword = "WrongPass1" });

        ask.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangeEmail_ToAnAddressThatIsTaken_IsAConflict()
    {
        var ana = await Register("ana");
        var luis = await Register("luis");

        var ask = await Send(HttpMethod.Post, "/api/users/me/email", ana.Token, new { newEmail = luis.User.Email, currentPassword = ana.Password });

        ask.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ChangeEmail_ToTheSameAddress_IsABadRequest()
    {
        var ana = await Register("ana");

        var ask = await Send(HttpMethod.Post, "/api/users/me/email", ana.Token, new { newEmail = ana.User.Email.ToUpperInvariant(), currentPassword = ana.Password });

        ask.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangeEmail_IfSomeoneTakesTheAddressBeforeTheLinkIsOpened_IsAConflict()
    {
        var ana = await Register("ana");
        var newEmail = $"race-{Guid.NewGuid():N}@mail.com";

        (await Send(HttpMethod.Post, "/api/users/me/email", ana.Token, new { newEmail, currentPassword = ana.Password })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var token = _factory.Emails.LastTokenFor(newEmail);

        (await Send(HttpMethod.Post, "/api/auth/register", body: new { email = newEmail, password = "Password1", acceptedTerms = true, displayName = "Quick" })).StatusCode.Should().Be(HttpStatusCode.Created);

        (await Send(HttpMethod.Post, "/api/auth/confirm-email-change", body: new { token })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- Delete the account ----

    [Fact]
    public async Task DeleteAccount_RemovesTheAds_AndNobodyCanSignInAgain()
    {
        var ana = await Register("ana");
        var ad = await CreateAd(ana.Token, "Anuncio de ana borrable");

        var delete = await Send(HttpMethod.Delete, "/api/users/me", ana.Token, new { password = ana.Password });
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Send(HttpMethod.Get, $"/api/ads/{ad.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Post, "/api/auth/login", body: new { email = ana.User.Email, password = ana.Password })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Post, "/api/auth/refresh", body: new { refreshToken = ana.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The address is free again.
        (await Send(HttpMethod.Post, "/api/auth/register", body: new { email = ana.User.Email, password = "Password1", acceptedTerms = true, displayName = "Ana again" })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task DeleteAccount_WithAWrongPassword_IsABadRequest_AndKeepsTheAccount()
    {
        var ana = await Register("ana");

        (await Send(HttpMethod.Delete, "/api/users/me", ana.Token, new { password = "WrongPass1" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await Send(HttpMethod.Get, "/api/users/me", ana.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteAccount_OfAnAdmin_IsRefused()
    {
        var admin = await AdminToken();

        (await Send(HttpMethod.Delete, "/api/users/me", admin, new { password = ApiFactory.AdminPassword })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteAccount_KeepsTheChatsWithTheUserShownAsDeleted_AndNobodyCanWriteInThem()
    {
        var ana = await Register("ana");
        var luis = await Register("luis");
        var ad = await CreateAd(ana.Token, "Anuncio con chat");

        var started = await Send(HttpMethod.Post, "/api/conversations", luis.Token, new { adId = ad.Id });
        var conversation = (await started.Content.ReadFromJsonAsync<ConversationDto>())!;
        (await Send(HttpMethod.Post, $"/api/conversations/{conversation.Id}/messages", luis.Token, new { body = "hola" })).StatusCode.Should().Be(HttpStatusCode.Created);

        (await Send(HttpMethod.Delete, "/api/users/me", ana.Token, new { password = ana.Password })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = (await (await Send(HttpMethod.Get, "/api/conversations", luis.Token)).Content.ReadFromJsonAsync<PagedResult<ConversationDto>>())!;
        var kept = list.Items.Single(c => c.Id == conversation.Id);
        kept.OtherParticipant.Deleted.Should().BeTrue();
        kept.OtherParticipant.DisplayName.Should().NotContain("ana");

        (await Send(HttpMethod.Post, $"/api/conversations/{conversation.Id}/messages", luis.Token, new { body = "sigues?" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- Block users ----

    [Fact]
    public async Task Block_StopsBothSidesFromWritingInTheChat_AndUnblockingBringsItBack()
    {
        var ana = await Register("ana");
        var luis = await Register("luis");
        var ad = await CreateAd(ana.Token, "Anuncio bloqueo");

        var conversation = (await (await Send(HttpMethod.Post, "/api/conversations", luis.Token, new { adId = ad.Id })).Content.ReadFromJsonAsync<ConversationDto>())!;
        (await Send(HttpMethod.Post, $"/api/conversations/{conversation.Id}/messages", luis.Token, new { body = "hola" })).StatusCode.Should().Be(HttpStatusCode.Created);

        (await Send(HttpMethod.Put, $"/api/blocks/{luis.User.Id}", ana.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The blocked one and the blocker both get the same refusal.
        var fromLuis = await Send(HttpMethod.Post, $"/api/conversations/{conversation.Id}/messages", luis.Token, new { body = "me oyes?" });
        var fromAna = await Send(HttpMethod.Post, $"/api/conversations/{conversation.Id}/messages", ana.Token, new { body = "no" });
        fromLuis.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fromAna.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Opening the chat from the ad is refused too, and the history stays readable.
        (await Send(HttpMethod.Post, "/api/conversations", luis.Token, new { adId = ad.Id })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Get, $"/api/conversations/{conversation.Id}/messages", ana.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Send(HttpMethod.Delete, $"/api/blocks/{luis.User.Id}", ana.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Send(HttpMethod.Post, $"/api/conversations/{conversation.Id}/messages", luis.Token, new { body = "de nuevo" })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Block_ListsOnlyWhoYouBlocked_AndNeverWhoBlockedYou()
    {
        var ana = await Register("ana");
        var luis = await Register("luis");

        (await Send(HttpMethod.Put, $"/api/blocks/{luis.User.Id}", ana.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Send(HttpMethod.Put, $"/api/blocks/{luis.User.Id}", ana.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var mine = (await (await Send(HttpMethod.Get, "/api/blocks", ana.Token)).Content.ReadFromJsonAsync<List<BlockedUserDto>>())!;
        mine.Should().ContainSingle().Which.Id.Should().Be(luis.User.Id);

        var theirs = (await (await Send(HttpMethod.Get, "/api/blocks", luis.Token)).Content.ReadFromJsonAsync<List<BlockedUserDto>>())!;
        theirs.Should().BeEmpty();
    }

    [Fact]
    public async Task Block_Yourself_IsAConflict_AndAnUnknownUserIsNotFound()
    {
        var ana = await Register("ana");

        (await Send(HttpMethod.Put, $"/api/blocks/{ana.User.Id}", ana.Token)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Send(HttpMethod.Put, $"/api/blocks/{Guid.NewGuid()}", ana.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, "/api/blocks")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- Featured ads ----

    [Fact]
    public async Task Featuring_ShowsTheAdFirst_AndStopsWhenRemoved()
    {
        var ana = await Register("ana");
        var first = await CreateAd(ana.Token, "Destacable primero");
        await CreateAd(ana.Token, "Destacable segundo");
        var newest = await CreateAd(ana.Token, "Destacable tercero");

        (await SearchTitles("Destacable")).First().Should().Be(newest.Title);

        var feature = await Send(HttpMethod.Post, $"/api/ads/{first.Id}/feature", ana.Token, new { days = 7 });
        feature.StatusCode.Should().Be(HttpStatusCode.OK);
        var featured = (await feature.Content.ReadFromJsonAsync<AdDto>())!;
        featured.FeaturedUntilUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromMinutes(1));

        (await SearchTitles("Destacable")).First().Should().Be(first.Title);

        var unfeature = await Send(HttpMethod.Delete, $"/api/ads/{first.Id}/feature", ana.Token);
        (await unfeature.Content.ReadFromJsonAsync<AdDto>())!.FeaturedUntilUtc.Should().BeNull();
        (await SearchTitles("Destacable")).First().Should().Be(newest.Title);
    }

    [Fact]
    public async Task Featuring_AddsTheDaysToWhatIsLeft_ButNeverBeyondTheLimit()
    {
        var ana = await Register("ana");
        var ad = await CreateAd(ana.Token);

        await Send(HttpMethod.Post, $"/api/ads/{ad.Id}/feature", ana.Token, new { days = 30 });
        var again = await Send(HttpMethod.Post, $"/api/ads/{ad.Id}/feature", ana.Token, new { days = 30 });
        var twice = (await again.Content.ReadFromJsonAsync<AdDto>())!;
        twice.FeaturedUntilUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(60), TimeSpan.FromMinutes(1));

        await Send(HttpMethod.Post, $"/api/ads/{ad.Id}/feature", ana.Token, new { days = 30 });
        var capped = (await (await Send(HttpMethod.Post, $"/api/ads/{ad.Id}/feature", ana.Token, new { days = 30 })).Content.ReadFromJsonAsync<AdDto>())!;
        capped.FeaturedUntilUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(FeaturingRules.MaxDaysAhead), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Featuring_ADurationThatIsNotAPlan_IsABadRequest()
    {
        var ana = await Register("ana");
        var ad = await CreateAd(ana.Token);

        (await Send(HttpMethod.Post, $"/api/ads/{ad.Id}/feature", ana.Token, new { days = 5 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Featuring_AnAdOfSomeoneElse_IsForbidden_ButAnAdminCanDoIt()
    {
        var ana = await Register("ana");
        var luis = await Register("luis");
        var ad = await CreateAd(ana.Token);

        (await Send(HttpMethod.Post, $"/api/ads/{ad.Id}/feature", luis.Token, new { days = 3 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Delete, $"/api/ads/{ad.Id}/feature", luis.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = await AdminToken();
        (await Send(HttpMethod.Post, $"/api/ads/{ad.Id}/feature", admin, new { days = 3 })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(HttpMethod.Delete, $"/api/ads/{ad.Id}/feature", admin)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Featuring_RequiresASession()
    {
        (await Send(HttpMethod.Post, $"/api/ads/{Guid.NewGuid()}/feature", body: new { days = 3 })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Featuring_Options_ArePublic()
    {
        var response = await Send(HttpMethod.Get, "/api/featuring");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var options = (await response.Content.ReadFromJsonAsync<FeaturingOptionsDto>())!;
        options.Plans.Should().Equal(FeaturingRules.Plans);
        options.OwnersCanFeature.Should().BeTrue(); // the tests run as Development
    }
}
