using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Chat;
using Tulaziva.Application.Common;
using Tulaziva.Application.Users;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Api;

/// <summary>The chat as a client sees it, above all who can and who cannot see what.</summary>
public class ChatApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ChatApiTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---- Helpers ----

    private HttpRequestMessage Request(HttpMethod method, string url, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body != null)
            request.Content = JsonContent.Create(body);

        return request;
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, string? token, object? body = null) =>
        _client.SendAsync(Request(method, url, token, body));

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

    private async Task<string> AdminToken()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!.Token;
    }

    private async Task<AdDto> CreateAd(string token)
    {
        var response = await Send(HttpMethod.Post, "/api/ads", token,
            new { categoryId = 1, title = "Bicicleta de montana", description = "Casi nueva", price = 1500, currency = "MXN" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AdDto>())!;
    }

    private async Task<ConversationDto> StartChat(string token, Guid adId)
    {
        var response = await Send(HttpMethod.Post, "/api/conversations", token, new { adId });
        response.IsSuccessStatusCode.Should().BeTrue();
        return (await response.Content.ReadFromJsonAsync<ConversationDto>())!;
    }

    private async Task<MessageDto> SendText(string token, Guid chatId, string body)
    {
        var response = await Send(HttpMethod.Post, $"/api/conversations/{chatId}/messages", token, new { body });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<MessageDto>())!;
    }

    private Task<HttpResponseMessage> SendFiles(string token, Guid chatId, string? body, params (string Name, byte[] Bytes)[] files)
    {
        var form = new MultipartFormDataContent();

        if (body != null)
            form.Add(new StringContent(body), "body");

        foreach (var (name, bytes) in files)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); // what the client says is not trusted
            form.Add(content, "files", name);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{chatId}/messages/files") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return _client.SendAsync(request);
    }

    private string[] StoredChatFiles()
    {
        var folder = Path.Combine(_factory.StoragePath, "chat");
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories) : Array.Empty<string>();
    }

    private async Task<(AuthResponse Owner, AuthResponse Buyer, AdDto Ad, ConversationDto Chat)> NewChat()
    {
        var owner = await Register("owner");
        var buyer = await Register("buyer");
        var ad = await CreateAd(owner.Token);
        var chat = await StartChat(buyer.Token, ad.Id);

        return (owner, buyer, ad, chat);
    }

    // ---- Starting and talking ----

    [Fact]
    public async Task StartingAChat_NeedsASession_IsIdempotent_AndIsNotAllowedOnYourOwnAd()
    {
        var owner = await Register("owner");
        var buyer = await Register("buyer");
        var ad = await CreateAd(owner.Token);

        (await Send(HttpMethod.Post, "/api/conversations", null, new { adId = ad.Id })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var first = await Send(HttpMethod.Post, "/api/conversations", buyer.Token, new { adId = ad.Id });
        var second = await Send(HttpMethod.Post, "/api/conversations", buyer.Token, new { adId = ad.Id });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadFromJsonAsync<ConversationDto>())!.Id
            .Should().Be((await first.Content.ReadFromJsonAsync<ConversationDto>())!.Id);

        (await Send(HttpMethod.Post, "/api/conversations", owner.Token, new { adId = ad.Id })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Send(HttpMethod.Post, "/api/conversations", buyer.Token, new { adId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TwoPeopleTalk_AndEachSeesTheirOwnUnreadCount_UntilTheyMarkTheChatAsRead()
    {
        var (owner, buyer, _, chat) = await NewChat();

        await SendText(buyer.Token, chat.Id, "Hola, sigue disponible?");
        await SendText(buyer.Token, chat.Id, "Me interesa");

        var ownerList = await _client.SendAsync(Request(HttpMethod.Get, "/api/conversations", owner.Token));
        var item = (await ownerList.Content.ReadFromJsonAsync<PagedResult<ConversationDto>>())!.Items.Single(c => c.Id == chat.Id);
        item.UnreadCount.Should().Be(2);
        item.OtherParticipant.DisplayName.Should().Be("buyer");
        item.LastMessage!.Text.Should().Be("Me interesa");

        var unread = await _client.SendAsync(Request(HttpMethod.Get, "/api/conversations/unread-count", owner.Token));
        (await unread.Content.ReadFromJsonAsync<UnreadCountDto>())!.Count.Should().Be(2);

        // Reading the messages does not mark them as read; the explicit call does.
        var messages = await _client.SendAsync(Request(HttpMethod.Get, $"/api/conversations/{chat.Id}/messages", owner.Token));
        var page = (await messages.Content.ReadFromJsonAsync<PagedResult<MessageDto>>())!;
        page.Items.Select(m => m.Body).Should().Equal("Me interesa", "Hola, sigue disponible?");
        (await (await Send(HttpMethod.Get, "/api/conversations/unread-count", owner.Token)).Content.ReadFromJsonAsync<UnreadCountDto>())!.Count.Should().Be(2);

        (await Send(HttpMethod.Post, $"/api/conversations/{chat.Id}/read", owner.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await Send(HttpMethod.Get, "/api/conversations/unread-count", owner.Token)).Content.ReadFromJsonAsync<UnreadCountDto>())!.Count.Should().Be(0);

        await SendText(owner.Token, chat.Id, "Si, sigue disponible");
        (await (await Send(HttpMethod.Get, "/api/conversations/unread-count", buyer.Token)).Content.ReadFromJsonAsync<UnreadCountDto>())!.Count.Should().Be(1);
    }

    [Fact]
    public async Task AnEmptyOrTooLongMessage_Answers400()
    {
        var (_, buyer, _, chat) = await NewChat();

        (await Send(HttpMethod.Post, $"/api/conversations/{chat.Id}/messages", buyer.Token, new { body = "   " })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Post, $"/api/conversations/{chat.Id}/messages", buyer.Token, new { body = new string('a', ChatRules.MaxMessageLength + 1) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Post, $"/api/conversations/{chat.Id}/messages", buyer.Token, new { body = (string?)null })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- Privacy: the conversation ----

    [Fact]
    public async Task AThirdUser_AndAnAdmin_CannotSeeOrUseAConversation()
    {
        var (_, buyer, _, chat) = await NewChat();
        await SendText(buyer.Token, chat.Id, "words only the two of us may read");

        var stranger = await Register("stranger");
        var admin = await AdminToken();

        foreach (var token in new[] { stranger.Token, admin })
        {
            (await Send(HttpMethod.Get, $"/api/conversations/{chat.Id}", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await Send(HttpMethod.Get, $"/api/conversations/{chat.Id}/messages", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await Send(HttpMethod.Post, $"/api/conversations/{chat.Id}/messages", token, new { body = "I am listening" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await Send(HttpMethod.Post, $"/api/conversations/{chat.Id}/read", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);

            var list = await (await Send(HttpMethod.Get, "/api/conversations", token)).Content.ReadFromJsonAsync<PagedResult<ConversationDto>>();
            list!.Items.Should().NotContain(c => c.Id == chat.Id);
        }

        (await Send(HttpMethod.Get, $"/api/conversations/{chat.Id}/messages", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- Files ----

    [Fact]
    public async Task Files_CanBeSentAndDownloaded_ByTheTwoParticipantsAndTheAdmins_ButNotByAnyoneElse()
    {
        var (owner, buyer, _, chat) = await NewChat();
        var stranger = await Register("stranger");
        var admin = await AdminToken();

        var sent = await SendFiles(buyer.Token, chat.Id, "El contrato y la foto",
            ("C:\\docs\\contrato final.jpg", TestImages.Jpeg()), ("foto.PNG", TestImages.Png()));

        sent.StatusCode.Should().Be(HttpStatusCode.Created);
        var message = (await sent.Content.ReadFromJsonAsync<MessageDto>())!;
        message.Body.Should().Be("El contrato y la foto");
        var first = message.Attachments.Single(a => a.FileName == "contrato final.jpg");
        var png = message.Attachments.Single(a => a.FileName == "foto.PNG");
        first.ContentType.Should().Be("image/jpeg");
        png.ContentType.Should().Be("image/png");

        foreach (var token in new[] { buyer.Token, owner.Token, admin })
        {
            var download = await Send(HttpMethod.Get, first.Url, token);

            download.StatusCode.Should().Be(HttpStatusCode.OK);
            download.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
            download.Content.Headers.ContentDisposition?.DispositionType.Should().NotBe("attachment"); // an image is shown in the page
            download.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
            (await download.Content.ReadAsByteArrayAsync()).Should().Equal(TestImages.Jpeg());
        }

        var image = await Send(HttpMethod.Get, png.Url, owner.Token);
        image.StatusCode.Should().Be(HttpStatusCode.OK);
        image.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await image.Content.ReadAsByteArrayAsync()).Should().Equal(TestImages.Png());

        (await Send(HttpMethod.Get, first.Url, stranger.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, first.Url, null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Get, $"/api/conversations/{Guid.NewGuid()}/attachments/{first.Id}", buyer.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DangerousOrDisguisedFiles_AreRefused_AndNothingIsStored()
    {
        var (_, buyer, _, chat) = await NewChat();
        var before = StoredChatFiles().Length;

        // Only .jpg, .jpeg and .png: documents, zip files, other image formats and programs are refused by their extension,
        // even when what is inside is a real image.
        foreach (var name in new[] { "contract.pdf", "archive.zip", "report.docx", "sheet.xlsx", "notes.txt", "anim.gif", "pic.webp", "setup.exe", "page.html" })
            (await SendFiles(buyer.Token, chat.Id, null, (name, TestImages.Png()))).StatusCode.Should().Be(HttpStatusCode.BadRequest, name);

        // A program with a program's name, a program renamed to .jpg, a web page, an image that is really text.
        (await SendFiles(buyer.Token, chat.Id, null, ("setup.exe", TestFiles.Exe()))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendFiles(buyer.Token, chat.Id, null, ("factura.jpg", TestFiles.Exe()))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendFiles(buyer.Token, chat.Id, null, ("page.html", TestFiles.Text()))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendFiles(buyer.Token, chat.Id, null, ("photo.png", TestFiles.Text()))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // One bad file spoils the whole message, even next to a good one.
        (await SendFiles(buyer.Token, chat.Id, "hi", ("good.jpg", TestImages.Jpeg()), ("bad.jpg", TestFiles.Exe()))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Too many files, and nothing at all.
        var tooMany = Enumerable.Range(0, ChatRules.MaxFilesPerMessage + 1).Select(i => ($"f{i}.jpg", TestImages.Jpeg())).ToArray();
        (await SendFiles(buyer.Token, chat.Id, null, tooMany)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendFiles(buyer.Token, chat.Id, null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        StoredChatFiles().Length.Should().Be(before);

        var messages = await (await Send(HttpMethod.Get, $"/api/conversations/{chat.Id}/messages", buyer.Token)).Content.ReadFromJsonAsync<PagedResult<MessageDto>>();
        messages!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task AFileSentByAStranger_IsRefused()
    {
        var (_, _, _, chat) = await NewChat();
        var stranger = await Register("stranger");

        (await SendFiles(stranger.Token, chat.Id, "hi", ("a.jpg", TestImages.Jpeg()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Admins ----

    [Fact]
    public async Task TheAdminListOfFiles_ShowsWhoSentWhat_NeverTheText_AndIsOnlyForAdmins()
    {
        var (_, buyer, _, chat) = await NewChat();
        await SendFiles(buyer.Token, chat.Id, "this text is private", ("a.jpg", TestImages.Jpeg()));
        var admin = await AdminToken();

        (await Send(HttpMethod.Get, "/api/admin/attachments", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Get, "/api/admin/attachments", buyer.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var response = await Send(HttpMethod.Get, "/api/admin/attachments?page=1&pageSize=100", admin);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("a.jpg").And.NotContain("this text is private");

        var page = await Send(HttpMethod.Get, "/api/admin/attachments?page=1&pageSize=100", admin);
        var item = (await page.Content.ReadFromJsonAsync<PagedResult<AdminAttachmentDto>>())!.Items.First(a => a.ConversationId == chat.Id);
        item.SenderDisplayName.Should().Be("buyer");
        (await Send(HttpMethod.Get, item.Url, admin)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- The ad goes away, the chat stays ----

    [Fact]
    public async Task TheChat_SurvivesWhenTheOwnerDeletesTheAd()
    {
        var (owner, buyer, ad, chat) = await NewChat();
        await SendText(buyer.Token, chat.Id, "before the ad disappeared");

        (await Send(HttpMethod.Delete, $"/api/ads/{ad.Id}", owner.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var conversation = await (await Send(HttpMethod.Get, $"/api/conversations/{chat.Id}", buyer.Token)).Content.ReadFromJsonAsync<ConversationDto>();
        conversation!.AdTitle.Should().Be("Bicicleta de montana");

        var messages = await Send(HttpMethod.Get, $"/api/conversations/{chat.Id}/messages", owner.Token);
        messages.StatusCode.Should().Be(HttpStatusCode.OK);
        (await messages.Content.ReadFromJsonAsync<PagedResult<MessageDto>>())!.Items.Should().ContainSingle();

        // A new chat about an ad that no longer exists cannot be started.
        (await Send(HttpMethod.Post, "/api/conversations", (await Register("late")).Token, new { adId = ad.Id })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EveryResponse_CarriesTheNoSniffHeader()
    {
        var response = await _client.GetAsync("/api/categories");

        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
    }
}
