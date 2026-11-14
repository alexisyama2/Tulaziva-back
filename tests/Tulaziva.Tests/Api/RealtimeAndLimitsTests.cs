using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.SignalR.Client;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Chat;
using Tulaziva.Application.Common;
using Tulaziva.Application.Users;

namespace Tulaziva.Tests.Api;

/// <summary>The live channel of the chats (SignalR, with the real token validation) and the limit on the endpoints that send emails.</summary>
public class RealtimeAndLimitsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public RealtimeAndLimitsTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---- Helpers ----

    private async Task<AuthResponse> Register(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new { email = $"{name}-{Guid.NewGuid():N}@mail.com", password = "Password1", acceptedTerms = true, displayName = name });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = body == null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await _client.SendAsync(request);
    }

    /// <summary>Long polling because the in-memory test server has no real sockets; the token travels like the browser sends it.</summary>
    private HubConnection Connect(string? token) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/chat"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult(token);
            })
            .Build();

    private async Task<(AuthResponse Owner, AuthResponse Luis, ConversationDto Chat)> ChatBetweenTwoUsers()
    {
        var owner = await Register("owner");
        var luis = await Register("luis");

        var adResponse = await Send(HttpMethod.Post, "/api/ads", owner.Token, new { categoryId = 1, title = "Bicicleta", description = "Casi nueva", price = 1500, currency = "MXN" });
        var ad = (await adResponse.Content.ReadFromJsonAsync<AdDto>())!;

        var chatResponse = await Send(HttpMethod.Post, "/api/conversations", luis.Token, new { adId = ad.Id });
        var chat = (await chatResponse.Content.ReadFromJsonAsync<ConversationDto>())!;

        return (owner, luis, chat);
    }

    // ---- Live channel ----

    [Fact]
    public async Task ANewMessage_ArrivesLiveToTheOtherParticipant_AndOnlyToThem()
    {
        var (owner, luis, chat) = await ChatBetweenTwoUsers();

        var received = new TaskCompletionSource<MessageNotification>();
        var senderGotSomething = false;

        await using var ownerConnection = Connect(owner.Token);
        await using var luisConnection = Connect(luis.Token);
        ownerConnection.On<MessageNotification>("MessageReceived", n => received.TrySetResult(n));
        luisConnection.On<MessageNotification>("MessageReceived", _ => senderGotSomething = true);
        await ownerConnection.StartAsync();
        await luisConnection.StartAsync();

        var send = await Send(HttpMethod.Post, $"/api/conversations/{chat.Id}/messages", luis.Token, new { body = "Sigue disponible?" });
        send.StatusCode.Should().Be(HttpStatusCode.Created);

        var notification = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        notification.Message.Body.Should().Be("Sigue disponible?");
        notification.Message.ConversationId.Should().Be(chat.Id);
        notification.SenderDisplayName.Should().Be("luis");
        notification.AdTitle.Should().Be("Bicicleta");

        await Task.Delay(300);
        senderGotSomething.Should().BeFalse("the sender already has the message in the answer of the request");
    }

    [Fact]
    public async Task MarkingAsRead_TellsTheSenderLive()
    {
        var (owner, luis, chat) = await ChatBetweenTwoUsers();
        await Send(HttpMethod.Post, $"/api/conversations/{chat.Id}/messages", luis.Token, new { body = "Hola" });

        var read = new TaskCompletionSource<Guid>();
        await using var luisConnection = Connect(luis.Token);
        luisConnection.On<Guid>("ConversationRead", id => read.TrySetResult(id));
        await luisConnection.StartAsync();

        (await Send(HttpMethod.Post, $"/api/conversations/{chat.Id}/read", owner.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await read.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(chat.Id);
    }

    [Fact]
    public async Task TheLiveChannel_RefusesAnonymousAndInvalidTokens()
    {
        await using var anonymous = Connect(null);
        await using var forged = Connect("not-a-real-token");

        var withoutToken = async () => await anonymous.StartAsync();
        var withBadToken = async () => await forged.StartAsync();

        await withoutToken.Should().ThrowAsync<HttpRequestException>();
        await withBadToken.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SendingStillWorks_WhenNobodyIsConnected()
    {
        var (_, luis, chat) = await ChatBetweenTwoUsers();

        var response = await Send(HttpMethod.Post, $"/api/conversations/{chat.Id}/messages", luis.Token, new { body = "Nadie escucha" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ---- Limit on the endpoints that send emails ----

    [Fact]
    public async Task ForgotPassword_IsLimitedPerClient()
    {
        using var limited = _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:AccountEmailsPermitLimit"] = "2" })));
        var client = limited.CreateClient();

        async Task<HttpStatusCode> Ask() =>
            (await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "someone@mail.com" })).StatusCode;

        (await Ask()).Should().Be(HttpStatusCode.NoContent);
        (await Ask()).Should().Be(HttpStatusCode.NoContent);
        (await Ask()).Should().Be(HttpStatusCode.TooManyRequests);
    }
}
