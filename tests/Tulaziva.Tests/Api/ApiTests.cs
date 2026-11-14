using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Common;
using Tulaziva.Application.Users;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Api;

/// <summary>The API as a client sees it: real routes, token authentication, error answers and the language of the messages.</summary>
public class ApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ApiTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---- Helpers ----

    private static string UniqueEmail(string name) => $"{name}-{Guid.NewGuid():N}@mail.com";

    private HttpRequestMessage Request(HttpMethod method, string url, string? token = null, object? body = null, string? language = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (language != null)
            request.Headers.AcceptLanguage.ParseAdd(language);

        if (body != null)
            request.Content = JsonContent.Create(body);

        return request;
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, string? token = null, object? body = null, string? language = null) =>
        _client.SendAsync(Request(method, url, token, body, language));

    private async Task<AuthResponse> Register(string name, double? latitude = null, double? longitude = null)
    {
        var response = await Send(HttpMethod.Post, "/api/auth/register", body: new
        {
            email = UniqueEmail(name),
            password = "Password1",
            acceptedTerms = true, displayName = name,
            latitude,
            longitude
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private async Task<string> AdminToken()
    {
        var response = await Send(HttpMethod.Post, "/api/auth/login", body: new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!.Token;
    }

    private async Task<AdDto> CreateAd(string token, string title = "Bicicleta de montaña", double? latitude = null, double? longitude = null)
    {
        var response = await Send(HttpMethod.Post, "/api/ads", token, new
        {
            categoryId = 1,
            title,
            description = "Casi nueva",
            price = 1500,
            currency = "mxn",
            latitude,
            longitude
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AdDto>())!;
    }

    private Task<HttpResponseMessage> Upload(string token, Guid adId, byte[] bytes, string fileName = "photo.jpg")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/ads/{adId}/images") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return _client.SendAsync(request);
    }

    // ---- Start-up ----

    [Fact]
    public async Task TheSwaggerDocumentIsBuiltAndListsTheEndpoints()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("/api/auth/login").And.Contain("/api/ads").And.Contain("/api/admin/users").And.Contain("bearer")
            .And.Contain("/api/conversations").And.Contain("/api/conversations/{id}/messages/files").And.Contain("/api/admin/attachments")
            .And.Contain("/api/users/me/photo").And.Contain("/api/users/{id}/photo");
    }

    // ---- Accounts ----

    [Fact]
    public async Task RegisterThenLogin_ThenAskForTheOwnProfile()
    {
        var registered = await Register("ana", 19.4326, -99.1332);
        registered.User.Role.Should().Be("User");
        registered.User.Latitude.Should().Be(19.4326);

        var login = await Send(HttpMethod.Post, "/api/auth/login", body: new { email = registered.User.Email, password = "Password1" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await login.Content.ReadFromJsonAsync<AuthResponse>())!.Token;

        var me = await Send(HttpMethod.Get, "/api/users/me", token);
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        (await me.Content.ReadFromJsonAsync<UserDto>())!.Email.Should().Be(registered.User.Email);
    }

    [Fact]
    public async Task Me_WithoutAToken_OrWithAGarbageToken_Answers401()
    {
        (await Send(HttpMethod.Get, "/api/users/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Get, "/api/users/me", "not-a-token")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithWrongCredentials_Answers401InTheLanguageOfTheRequest()
    {
        var user = await Register("luis");

        var spanish = await Send(HttpMethod.Post, "/api/auth/login", body: new { email = user.User.Email, password = "Wrong12345" }, language: "es");
        var english = await Send(HttpMethod.Post, "/api/auth/login", body: new { email = user.User.Email, password = "Wrong12345" }, language: "en");

        spanish.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        english.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await spanish.Content.ReadAsStringAsync()).Should().Contain("Correo o contraseña incorrectos.");
        (await english.Content.ReadAsStringAsync()).Should().Contain("Incorrect email or password.");
    }

    [Fact]
    public async Task Register_WithAnEmailThatExists_Answers409_AndWithBadData_Answers400WithTheErrorsByField()
    {
        var user = await Register("repeated");

        var duplicate = await Send(HttpMethod.Post, "/api/auth/register", body: new { email = user.User.Email.ToUpperInvariant(), password = "Password1", acceptedTerms = true, displayName = "Again" });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var invalid = await Send(HttpMethod.Post, "/api/auth/register", body: new { email = "nope", password = "1", acceptedTerms = true, displayName = "", latitude = 10 }, language: "en");
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errors = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        errors.EnumerateObject().Select(p => p.Name).Should().Contain(new[] { "Email", "Password", "DisplayName", "Location" });
    }

    [Fact]
    public async Task Register_WithoutAcceptingTheTerms_Answers400_AndCreatesNothing()
    {
        var email = $"noterms-{Guid.NewGuid():N}@mail.com";
        var response = await Send(HttpMethod.Post, "/api/auth/register", body: new { email, password = "Password1", displayName = "No Terms" }, language: "en");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("accept the Terms of use");

        var login = await Send(HttpMethod.Post, "/api/auth/login", body: new { email, password = "Password1" });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ABodyThatIsNotValidJson_Answers400_NotAServerError()
    {
        var notJson = new StringContent("{ this is not json", Encoding.UTF8, "application/json");
        var invalidUtf8 = new ByteArrayContent(new byte[] { 0x7B, 0x22, 0x65, 0x6D, 0x61, 0x69, 0x6C, 0x22, 0x3A, 0x22, 0xF1, 0x22, 0x7D });
        invalidUtf8.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        (await _client.PostAsync("/api/auth/login", notJson)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.PostAsync("/api/auth/login", invalidUtf8)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheAdminEndpoints_AreOnlyForAdmins()
    {
        var user = await Register("regular");
        var admin = await AdminToken();

        (await Send(HttpMethod.Get, "/api/admin/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Get, "/api/admin/users", user.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var list = await Send(HttpMethod.Get, "/api/admin/users?page=1&pageSize=5", admin);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<PagedResult<UserDto>>())!.TotalCount.Should().BeGreaterThan(0);

        var created = await Send(HttpMethod.Post, "/api/admin/users", admin, new { email = UniqueEmail("boss"), password = "Password1", acceptedTerms = true, displayName = "Boss" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        (await created.Content.ReadFromJsonAsync<UserDto>())!.Role.Should().Be("Admin");

        (await Send(HttpMethod.Post, "/api/admin/users", user.Token, new { email = UniqueEmail("x"), password = "Password1", acceptedTerms = true, displayName = "X" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- Ads ----

    [Fact]
    public async Task Categories_ComeInTheLanguageOfTheRequest()
    {
        var spanish = await (await Send(HttpMethod.Get, "/api/categories", language: "es")).Content.ReadAsStringAsync();
        var english = await (await Send(HttpMethod.Get, "/api/categories", language: "en")).Content.ReadAsStringAsync();

        spanish.Should().Contain("Vehículos");
        english.Should().Contain("Vehicles");
    }

    [Fact]
    public async Task AnAd_CanBeCreatedOnlyWithASession_AndTakesTheLocationOfItsOwner()
    {
        (await Send(HttpMethod.Post, "/api/ads", body: new { categoryId = 1 })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var ana = await Register("ana-ads", 19.4326, -99.1332);
        var ad = await CreateAd(ana.Token);

        ad.OwnerId.Should().Be(ana.User.Id);
        ad.Currency.Should().Be("MXN");
        ad.Latitude.Should().Be(19.4326);
        ad.Longitude.Should().Be(-99.1332);
        ad.ExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(AdRules.DurationDays), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task InvalidAdData_Answers400WithTheErrorsInTheLanguageOfTheRequest()
    {
        var ana = await Register("ana-invalid");

        var response = await Send(HttpMethod.Post, "/api/ads", ana.Token,
            new { categoryId = 999, title = "a", description = "", price = -1, currency = "XXX" }, language: "en");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("The category does not exist.").And.Contain("Title must be between 3 and 120 characters long.");
    }

    [Fact]
    public async Task AnImage_CanBeUploaded_AndDownloadedByAnyone_ButOnlyTheRealImagesAreAccepted()
    {
        var ana = await Register("ana-images");
        var ad = await CreateAd(ana.Token);

        var uploaded = await Upload(ana.Token, ad.Id, TestImages.Jpeg());
        uploaded.StatusCode.Should().Be(HttpStatusCode.Created);
        var image = (await uploaded.Content.ReadFromJsonAsync<AdImageDto>())!;
        image.ContentType.Should().Be("image/jpeg");

        // A text file named .jpg is refused.
        (await Upload(ana.Token, ad.Id, TestImages.Text())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // So is a real image with a name that is not an image.
        (await Upload(ana.Token, ad.Id, TestImages.Jpeg(), "photo.exe")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var download = await _client.GetAsync(image.Url);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(TestImages.Jpeg());

        Directory.GetFiles(_factory.StoragePath, "*.jpg", SearchOption.AllDirectories).Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task AnotherUser_CannotEditDeleteOrUploadToTheAd()
    {
        var ana = await Register("ana-owner");
        var luis = await Register("luis-intruder");
        var ad = await CreateAd(ana.Token);

        var edit = await Send(HttpMethod.Put, $"/api/ads/{ad.Id}", luis.Token,
            new { categoryId = 1, title = "Stolen", description = "x", price = 1, currency = "MXN" });
        edit.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await Send(HttpMethod.Delete, $"/api/ads/{ad.Id}", luis.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Post, $"/api/ads/{ad.Id}/renew", luis.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Upload(luis.Token, ad.Id, TestImages.Jpeg())).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var stillThere = await _client.GetFromJsonAsync<AdDto>($"/api/ads/{ad.Id}");
        stillThere!.Title.Should().Be("Bicicleta de montaña");
    }

    [Fact]
    public async Task Search_FindsTheAdByTextAndDistance_AndLeavesOutTheFarOnes()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var ana = await Register("ana-search", 19.4326, -99.1332);
        await CreateAd(ana.Token, title: $"Bicicleta {marker}");

        var near = await _client.GetFromJsonAsync<PagedResult<AdSummaryDto>>($"/api/ads?text={marker.ToUpperInvariant()}&latitude=19.43&longitude=-99.13&radiusKm=50");
        var far = await _client.GetFromJsonAsync<PagedResult<AdSummaryDto>>($"/api/ads?text={marker}&latitude=20.6597&longitude=-103.3496&radiusKm=50");

        near!.Items.Should().ContainSingle().Which.DistanceKm.Should().BeLessThan(5);
        far!.Items.Should().BeEmpty();

        (await _client.GetAsync("/api/ads?radiusKm=10")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnAdTakenDownByAnAdmin_DisappearsForThePublic_ButNotForItsOwnerOrTheAdmins()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var ana = await Register("ana-removed");
        var ad = await CreateAd(ana.Token, title: $"Casco {marker}");
        var admin = await AdminToken();

        (await Send(HttpMethod.Post, $"/api/admin/ads/{ad.Id}/remove", ana.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Post, $"/api/admin/ads/{ad.Id}/remove", admin)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Send(HttpMethod.Get, $"/api/ads/{ad.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, $"/api/ads/{ad.Id}", ana.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(HttpMethod.Get, $"/api/ads/{ad.Id}", admin)).StatusCode.Should().Be(HttpStatusCode.OK);

        var search = await _client.GetFromJsonAsync<PagedResult<AdSummaryDto>>($"/api/ads?text={marker}");
        search!.Items.Should().BeEmpty();

        var mine = await (await Send(HttpMethod.Get, "/api/ads/mine", ana.Token)).Content.ReadFromJsonAsync<PagedResult<AdSummaryDto>>();
        mine!.Items.Should().ContainSingle();

        (await Send(HttpMethod.Post, $"/api/ads/{ad.Id}/renew", ana.Token)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeletingAnAd_RemovesItsImageFilesFromTheDisk()
    {
        var ana = await Register("ana-delete");
        var ad = await CreateAd(ana.Token);
        var image = (await (await Upload(ana.Token, ad.Id, TestImages.Png(), "a.png")).Content.ReadFromJsonAsync<AdImageDto>())!;

        var folder = Path.Combine(_factory.StoragePath, "ads", ad.Id.ToString("N"));
        Directory.GetFiles(folder).Should().ContainSingle();

        (await Send(HttpMethod.Delete, $"/api/ads/{ad.Id}", ana.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        Directory.GetFiles(folder).Should().BeEmpty();
        (await _client.GetAsync($"/api/ads/{ad.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync(image.Url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
