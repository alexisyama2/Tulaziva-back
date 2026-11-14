using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Common;
using Tulaziva.Application.Reports;
using Tulaziva.Application.Users;

namespace Tulaziva.Tests.Api;

/// <summary>
/// Everything that came with the second round of features, through the real routes: verifying the address, forgotten passwords,
/// editing the profile, favorites and reports.
/// </summary>
public class AccountApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public AccountApiTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---- Helpers ----

    private sealed record Session(string Token, UserDto User, string Password);

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

        return new Session(auth.Token, auth.User, "Password1");
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

    private async Task<UserDto> Me(string token) =>
        (await (await Send(HttpMethod.Get, "/api/users/me", token)).Content.ReadFromJsonAsync<UserDto>())!;

    // ---- Verify email ----

    [Fact]
    public async Task SigningUp_SendsALinkThatVerifiesTheAddress_Once()
    {
        var ana = await Register("ana");
        ana.User.EmailVerified.Should().BeFalse();

        var token = _factory.Emails.LastTokenFor(ana.User.Email);
        var verify = await Send(HttpMethod.Post, "/api/auth/verify-email", body: new { token });

        verify.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Me(ana.Token)).EmailVerified.Should().BeTrue();

        var again = await Send(HttpMethod.Post, "/api/auth/verify-email", body: new { token });
        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Verify_WithAnInventedToken_IsABadRequestInTheLanguageOfTheRequest()
    {
        var response = await Send(HttpMethod.Post, "/api/auth/verify-email", body: new { token = "invented" }, language: "en");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("not valid or has expired");
    }

    [Fact]
    public async Task Resend_SendsAnotherLink_AndTheOldOneStopsWorking()
    {
        var ana = await Register("ana");
        var first = _factory.Emails.LastTokenFor(ana.User.Email);

        (await Send(HttpMethod.Post, "/api/users/me/resend-verification", ana.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var second = _factory.Emails.LastTokenFor(ana.User.Email);

        second.Should().NotBe(first);
        (await Send(HttpMethod.Post, "/api/auth/verify-email", body: new { token = first })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Post, "/api/auth/verify-email", body: new { token = second })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Resend_RequiresASession()
    {
        (await Send(HttpMethod.Post, "/api/users/me/resend-verification")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- Forgot password ----

    [Fact]
    public async Task ForgotPassword_Flow_ChangesThePasswordWithTheLinkOfTheEmail()
    {
        var ana = await Register("ana");

        (await Send(HttpMethod.Post, "/api/auth/forgot-password", body: new { email = ana.User.Email })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var token = _factory.Emails.LastTokenFor(ana.User.Email);

        var reset = await Send(HttpMethod.Post, "/api/auth/reset-password", body: new { token, newPassword = "Brand9New" });
        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Send(HttpMethod.Post, "/api/auth/login", body: new { email = ana.User.Email, password = "Password1" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Post, "/api/auth/login", body: new { email = ana.User.Email, password = "Brand9New" })).StatusCode.Should().Be(HttpStatusCode.OK);

        // The link works once.
        (await Send(HttpMethod.Post, "/api/auth/reset-password", body: new { token, newPassword = "Another9Pass" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_AnswersLikeAKnownOne_AndSendsNothing()
    {
        var before = _factory.Emails.Sent.Count;

        var response = await Send(HttpMethod.Post, "/api/auth/forgot-password", body: new { email = "nobody-at-all@mail.com" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _factory.Emails.Sent.Count.Should().Be(before);
    }

    [Fact]
    public async Task ForgotPassword_WithAnInvalidEmail_IsABadRequest()
    {
        (await Send(HttpMethod.Post, "/api/auth/forgot-password", body: new { email = "not-an-email" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ResetPassword_RefusesAWeakPasswordWithoutSpendingTheLink()
    {
        var ana = await Register("ana");
        await Send(HttpMethod.Post, "/api/auth/forgot-password", body: new { email = ana.User.Email });
        var token = _factory.Emails.LastTokenFor(ana.User.Email);

        (await Send(HttpMethod.Post, "/api/auth/reset-password", body: new { token, newPassword = "weak" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Post, "/api/auth/reset-password", body: new { token, newPassword = "Strong123" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---- Edit profile ----

    [Fact]
    public async Task UpdateProfile_ChangesNameLanguageAndLocation()
    {
        var ana = await Register("ana");

        var response = await Send(HttpMethod.Put, "/api/users/me", ana.Token, new { acceptedTerms = true, displayName = "Ana Maria", language = "en", latitude = 40.4, longitude = -3.7 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = (await response.Content.ReadFromJsonAsync<UserDto>())!;
        dto.DisplayName.Should().Be("Ana Maria");
        dto.PreferredLanguage.Should().Be("en");
        dto.Latitude.Should().Be(40.4);
        (await Me(ana.Token)).DisplayName.Should().Be("Ana Maria");
    }

    [Fact]
    public async Task UpdateProfile_Invalid_IsABadRequest_AndRequiresASession()
    {
        var ana = await Register("ana");

        (await Send(HttpMethod.Put, "/api/users/me", ana.Token, new { acceptedTerms = true, displayName = "", language = "fr" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Put, "/api/users/me", body: new { acceptedTerms = true, displayName = "X" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_NeedsTheCurrentOne_AndAWrongOneIsNot401()
    {
        var ana = await Register("ana");

        var wrong = await Send(HttpMethod.Post, "/api/users/me/password", ana.Token, new { currentPassword = "Nope12345", newPassword = "Brand9New" });
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a 401 would make the web app think the session expired");
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("CurrentPassword");

        var ok = await Send(HttpMethod.Post, "/api/users/me/password", ana.Token, new { currentPassword = "Password1", newPassword = "Brand9New" });
        ok.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Send(HttpMethod.Post, "/api/auth/login", body: new { email = ana.User.Email, password = "Brand9New" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- Favorites ----

    [Fact]
    public async Task Favorites_SaveListAndRemove()
    {
        var owner = await Register("owner");
        var luis = await Register("luis");
        var ad = await CreateAd(owner.Token);

        (await Send(HttpMethod.Put, $"/api/favorites/{ad.Id}", luis.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Send(HttpMethod.Put, $"/api/favorites/{ad.Id}", luis.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var ids = await (await Send(HttpMethod.Get, "/api/favorites/ids", luis.Token)).Content.ReadFromJsonAsync<List<Guid>>();
        ids.Should().Equal(ad.Id);

        var list = (await (await Send(HttpMethod.Get, "/api/favorites", luis.Token)).Content.ReadFromJsonAsync<PagedResult<AdSummaryDto>>())!;
        list.TotalCount.Should().Be(1);
        list.Items.Single().Title.Should().Be(ad.Title);

        // The other users do not see them.
        (await (await Send(HttpMethod.Get, "/api/favorites/ids", owner.Token)).Content.ReadFromJsonAsync<List<Guid>>()).Should().BeEmpty();

        (await Send(HttpMethod.Delete, $"/api/favorites/{ad.Id}", luis.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Send(HttpMethod.Delete, $"/api/favorites/{ad.Id}", luis.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await Send(HttpMethod.Get, "/api/favorites/ids", luis.Token)).Content.ReadFromJsonAsync<List<Guid>>()).Should().BeEmpty();
    }

    [Fact]
    public async Task Favorites_RequireASession_AndAnUnknownAdIsNotFound()
    {
        var luis = await Register("luis");

        (await Send(HttpMethod.Get, "/api/favorites")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Put, $"/api/favorites/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Put, $"/api/favorites/{Guid.NewGuid()}", luis.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Favorites_ListDoesNotShowTheAdsThatAreNoLongerPublic_AndTheyGoWhenTheAdIsDeleted()
    {
        var owner = await Register("owner");
        var luis = await Register("luis");
        var kept = await CreateAd(owner.Token, "Se queda");
        var deleted = await CreateAd(owner.Token, "Se borra");
        await Send(HttpMethod.Put, $"/api/favorites/{kept.Id}", luis.Token);
        await Send(HttpMethod.Put, $"/api/favorites/{deleted.Id}", luis.Token);

        (await Send(HttpMethod.Delete, $"/api/ads/{deleted.Id}", owner.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = (await (await Send(HttpMethod.Get, "/api/favorites", luis.Token)).Content.ReadFromJsonAsync<PagedResult<AdSummaryDto>>())!;
        list.Items.Select(a => a.Title).Should().Equal("Se queda");
    }

    // ---- Reports ----

    [Fact]
    public async Task Report_Flow_UserReports_AdminResolves_AdDisappears()
    {
        var owner = await Register("owner");
        var luis = await Register("luis");
        var admin = await AdminToken();
        var ad = await CreateAd(owner.Token, "Estafa segura");

        (await Send(HttpMethod.Post, $"/api/ads/{ad.Id}/report", luis.Token, new { reason = "fraud", details = "Pide dinero antes" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Send(HttpMethod.Post, $"/api/ads/{ad.Id}/report", luis.Token, new { reason = "spam" })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var open = (await (await Send(HttpMethod.Get, "/api/admin/reports?status=open&pageSize=100", admin)).Content.ReadFromJsonAsync<PagedResult<ReportDto>>())!;
        var report = open.Items.Should().ContainSingle(r => r.AdId == ad.Id).Subject;
        report.Reason.Should().Be("fraud");
        report.Details.Should().Be("Pide dinero antes");
        report.ReporterDisplayName.Should().Be("luis");
        report.AdState.Should().Be("active");

        var resolve = await Send(HttpMethod.Post, $"/api/admin/reports/{report.Id}/resolve", admin);
        resolve.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resolve.Content.ReadFromJsonAsync<ReportDto>())!.Status.Should().Be("resolved");

        (await Send(HttpMethod.Get, $"/api/ads/{ad.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, $"/api/ads/{ad.Id}", owner.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(HttpMethod.Post, $"/api/admin/reports/{report.Id}/dismiss", admin)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Report_Dismiss_LeavesTheAdPublic()
    {
        var owner = await Register("owner");
        var luis = await Register("luis");
        var admin = await AdminToken();
        var ad = await CreateAd(owner.Token);
        await Send(HttpMethod.Post, $"/api/ads/{ad.Id}/report", luis.Token, new { reason = "spam" });
        var report = (await (await Send(HttpMethod.Get, "/api/admin/reports?status=open&pageSize=100", admin)).Content.ReadFromJsonAsync<PagedResult<ReportDto>>())!.Items.Single(r => r.AdId == ad.Id);

        (await Send(HttpMethod.Post, $"/api/admin/reports/{report.Id}/dismiss", admin)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Send(HttpMethod.Get, $"/api/ads/{ad.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Report_Rules_OwnAdInvalidReasonAndAnonymous()
    {
        var owner = await Register("owner");
        var luis = await Register("luis");
        var ad = await CreateAd(owner.Token);

        (await Send(HttpMethod.Post, $"/api/ads/{ad.Id}/report", owner.Token, new { reason = "spam" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Send(HttpMethod.Post, $"/api/ads/{ad.Id}/report", luis.Token, new { reason = "boring" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Post, $"/api/ads/{ad.Id}/report", luis.Token, new { reason = "other" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Post, $"/api/ads/{ad.Id}/report", body: new { reason = "spam" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminReports_AreForAdminsOnly()
    {
        var luis = await Register("luis");

        (await Send(HttpMethod.Get, "/api/admin/reports", luis.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Post, $"/api/admin/reports/{Guid.NewGuid()}/resolve", luis.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Get, "/api/admin/reports")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Get, "/api/admin/reports?status=weird", await AdminToken())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
