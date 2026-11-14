using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Chat;
using Tulaziva.Application.Users;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Api;

/// <summary>The profile photo as a client sees it: who can set it, what is accepted, and where it shows up.</summary>
public class ProfilePhotoApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ProfilePhotoApiTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---- Helpers ----

    private async Task<AuthResponse> Register(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{name}-{Guid.NewGuid():N}@mail.com",
            password = "Password1",
            acceptedTerms = true, displayName = name
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private Task<HttpResponseMessage> UploadPhoto(string? token, string fileName, byte[]? bytes)
    {
        var form = new MultipartFormDataContent();

        if (bytes != null)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); // what the client says is not trusted
            form.Add(content, "file", fileName);
        }

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/users/me/photo") { Content = form };

        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, string? token)
    {
        var request = new HttpRequestMessage(method, url);

        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return _client.SendAsync(request);
    }

    private string[] StoredPhotos(Guid userId)
    {
        var folder = Path.Combine(_factory.StoragePath, "profiles", userId.ToString("N"));
        return Directory.Exists(folder) ? Directory.GetFiles(folder) : Array.Empty<string>();
    }

    private async Task<UserDto> SetPhoto(AuthResponse user, string fileName, byte[] bytes)
    {
        var response = await UploadPhoto(user.Token, fileName, bytes);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<UserDto>())!;
    }

    // ---- Upload and download ----

    [Fact]
    public async Task AUser_CanSetThePhoto_AndItShowsInTheProfileAndInTheLoginAnswer()
    {
        var ana = await Register("ana");
        ana.User.PhotoUrl.Should().BeNull();

        var updated = await SetPhoto(ana, "C:\\Users\\ana\\Pictures\\me.PNG", TestImages.Png());

        updated.PhotoUrl.Should().StartWith($"/api/users/{ana.User.Id}/photo?v=");

        var me = await (await Send(HttpMethod.Get, "/api/users/me", ana.Token)).Content.ReadFromJsonAsync<UserDto>();
        me!.PhotoUrl.Should().Be(updated.PhotoUrl);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = ana.User.Email, password = "Password1" });
        (await login.Content.ReadFromJsonAsync<AuthResponse>())!.User.PhotoUrl.Should().Be(updated.PhotoUrl);
    }

    [Fact]
    public async Task ThePhoto_IsPublic_ItIsServedAsAnImage_WithACacheHeaderAndNoSniffing()
    {
        var ana = await Register("ana-public");
        var updated = await SetPhoto(ana, "me.png", TestImages.Png());

        var photo = await _client.GetAsync(updated.PhotoUrl); // no token: the photo is shown next to the ads

        photo.StatusCode.Should().Be(HttpStatusCode.OK);
        photo.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        photo.Headers.CacheControl!.Public.Should().BeTrue();
        photo.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        (await photo.Content.ReadAsByteArrayAsync()).Should().Equal(TestImages.Png());
    }

    [Fact]
    public async Task SettingThePhoto_NeedsASession_AndAUserWithoutPhotoOrAnUnknownUserHasNoImage()
    {
        (await UploadPhoto(null, "me.png", TestImages.Png())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var ana = await Register("ana-none");
        (await _client.GetAsync($"/api/users/{ana.User.Id}/photo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/users/{Guid.NewGuid()}/photo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- What is accepted ----

    [Fact]
    public async Task OnlyJpgAndPngAreAccepted_DocumentsZipFilesAndProgramsAreRefused_AndNothingIsStored()
    {
        var ana = await Register("ana-refused");

        // By the extension, even with the bytes of a real image inside.
        foreach (var name in new[] { "contract.pdf", "archive.zip", "report.docx", "notes.txt", "anim.gif", "pic.webp", "setup.exe", "page.html", "noextension" })
            (await UploadPhoto(ana.Token, name, TestImages.Png())).StatusCode.Should().Be(HttpStatusCode.BadRequest, name);

        // By the content: a program, a document, a zip file or a text renamed to an image.
        foreach (var disguised in new[] { TestFiles.Exe(), TestFiles.Pdf(), TestFiles.Zip(), TestFiles.Text() })
        {
            (await UploadPhoto(ana.Token, "me.png", disguised)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await UploadPhoto(ana.Token, "me.jpg", disguised)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // A png named .jpg (and the other way round) is not what it says either.
        (await UploadPhoto(ana.Token, "me.jpg", TestImages.Png())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await UploadPhoto(ana.Token, "me.png", TestImages.Jpeg())).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Nothing at all.
        (await UploadPhoto(ana.Token, "", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        StoredPhotos(ana.User.Id).Should().BeEmpty();
        var me = await (await Send(HttpMethod.Get, "/api/users/me", ana.Token)).Content.ReadFromJsonAsync<UserDto>();
        me!.PhotoUrl.Should().BeNull();
    }

    // ---- Replace and remove ----

    [Fact]
    public async Task ANewPhoto_ReplacesTheOldOne_WithANewUrl_AndOnlyOneFileStaysOnDisk()
    {
        var ana = await Register("ana-replace");
        var first = await SetPhoto(ana, "one.png", TestImages.Png());
        StoredPhotos(ana.User.Id).Should().ContainSingle();

        await Task.Delay(5); // the version of the URL comes from the time of the change

        var second = await SetPhoto(ana, "two.jpg", TestImages.Jpeg());

        second.PhotoUrl.Should().NotBe(first.PhotoUrl);
        StoredPhotos(ana.User.Id).Should().ContainSingle().Which.Should().EndWith(".jpg");

        var photo = await _client.GetAsync(second.PhotoUrl);
        photo.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        (await photo.Content.ReadAsByteArrayAsync()).Should().Equal(TestImages.Jpeg());
    }

    [Fact]
    public async Task RemovingThePhoto_ClearsTheUrl_DeletesTheFile_AndTheImageIsGone()
    {
        var ana = await Register("ana-remove");
        var updated = await SetPhoto(ana, "me.png", TestImages.Png());

        var removed = await Send(HttpMethod.Delete, "/api/users/me/photo", ana.Token);

        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await removed.Content.ReadFromJsonAsync<UserDto>())!.PhotoUrl.Should().BeNull();
        StoredPhotos(ana.User.Id).Should().BeEmpty();
        (await _client.GetAsync(updated.PhotoUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Doing it again is harmless.
        (await Send(HttpMethod.Delete, "/api/users/me/photo", ana.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(HttpMethod.Delete, "/api/users/me/photo", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EachUserHasTheirOwnPhoto_ChangingOneDoesNotTouchTheOther()
    {
        var ana = await Register("ana-own");
        var luis = await Register("luis-own");
        var anaPhoto = await SetPhoto(ana, "ana.png", TestImages.Png());
        var luisPhoto = await SetPhoto(luis, "luis.jpg", TestImages.Jpeg());

        await Send(HttpMethod.Delete, "/api/users/me/photo", luis.Token);

        (await _client.GetAsync(anaPhoto.PhotoUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync(luisPhoto.PhotoUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Where the photo shows up ----

    [Fact]
    public async Task ThePhoto_ShowsNextToTheAd_AndInTheChatsOfTheOwner()
    {
        var owner = await Register("owner-photo");
        var buyer = await Register("buyer-photo");
        var ownerPhoto = await SetPhoto(owner, "owner.png", TestImages.Png());

        var adResponse = await Send(HttpMethod.Post, "/api/ads", owner.Token, new { categoryId = 1, title = "Bicicleta de montana", description = "Casi nueva", price = 1500, currency = "MXN" });
        var ad = (await adResponse.Content.ReadFromJsonAsync<AdDto>())!;
        ad.OwnerPhotoUrl.Should().Be(ownerPhoto.PhotoUrl);

        var detail = await _client.GetFromJsonAsync<AdDto>($"/api/ads/{ad.Id}");
        detail!.OwnerPhotoUrl.Should().Be(ownerPhoto.PhotoUrl);

        var chatResponse = await Send(HttpMethod.Post, "/api/conversations", buyer.Token, new { adId = ad.Id });
        var chat = (await chatResponse.Content.ReadFromJsonAsync<ConversationDto>())!;
        chat.OtherParticipant.PhotoUrl.Should().Be(ownerPhoto.PhotoUrl); // the buyer sees the owner's photo
        chat.OtherParticipant.DisplayName.Should().Be("owner-photo");

        var listForOwner = await (await Send(HttpMethod.Get, "/api/conversations", owner.Token)).Content.ReadFromJsonAsync<Application.Common.PagedResult<ConversationDto>>();
        listForOwner!.Items.Single(c => c.Id == chat.Id).OtherParticipant.PhotoUrl.Should().BeNull(); // the buyer has none
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, string token, object body)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await _client.SendAsync(request);
    }
}
