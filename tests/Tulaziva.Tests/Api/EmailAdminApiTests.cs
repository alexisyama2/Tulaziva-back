using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tulaziva.Application.Admin;
using Tulaziva.Application.Common;
using Tulaziva.Application.Users;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;
using Tulaziva.Infrastructure.Persistence;

namespace Tulaziva.Tests.Api;

/// <summary>The email log and the test email, which an admin uses to find out why the mail server is not sending.</summary>
public class EmailAdminApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public EmailAdminApiTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:AccountEmailsPermitLimit"] = "1000" })))
            .CreateClient();
    }

    private async Task<string> AdminToken()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!.Token;
    }

    private async Task<string> UserToken()
    {
        var email = $"plain-{Guid.NewGuid():N}@mail.com";
        var response = await _client.PostAsJsonAsync("/api/auth/register", new { email, password = "Password1", acceptedTerms = true, displayName = "Plain" });
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!.Token;
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body != null)
            request.Content = JsonContent.Create(body);

        return _client.SendAsync(request);
    }

    private void AddLog(string to, EmailStatus status, string? error = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TulazivaDbContext>();
        db.EmailLogs.Add(new EmailLog { ToAddress = to, Subject = "Verifica tu correo", Status = status, Attempts = 3, LastError = error });
        db.SaveChanges();
    }

    [Fact]
    public async Task TheEmailLog_ListsWhatHappenedToTheEmails_AndCanBeFilteredByStatus()
    {
        var failed = $"failed-{Guid.NewGuid():N}@mail.com";
        AddLog(failed, EmailStatus.Failed, "SmtpException: Authentication failed (status GeneralFailure)");
        AddLog($"sent-{Guid.NewGuid():N}@mail.com", EmailStatus.Sent);
        var admin = await AdminToken();

        var response = await Send(HttpMethod.Get, "/api/admin/email-logs?status=Failed&pageSize=100", admin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<PagedResult<EmailLogDto>>())!;
        page.Items.Should().OnlyContain(l => l.Status == "Failed");
        var row = page.Items.Single(l => l.To == failed);
        row.LastError.Should().Contain("Authentication failed");
        row.Attempts.Should().Be(3);

        var all = (await (await Send(HttpMethod.Get, "/api/admin/email-logs?pageSize=100", admin)).Content.ReadFromJsonAsync<PagedResult<EmailLogDto>>())!;
        all.Items.Select(l => l.Status).Should().Contain(new[] { "Failed", "Sent" });
    }

    [Fact]
    public async Task TheEmailLog_IsForAdminsOnly()
    {
        (await Send(HttpMethod.Get, "/api/admin/email-logs", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Get, "/api/admin/email-logs", await UserToken())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TheEmailLog_RefusesAStatusThatDoesNotExist()
    {
        var response = await Send(HttpMethod.Get, "/api/admin/email-logs?status=Nope", await AdminToken());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheTestEmail_WhenSmtpIsNotConfigured_SaysWhichSettingsAreMissing()
    {
        var response = await Send(HttpMethod.Post, "/api/admin/email-test", await AdminToken(), new { to = "me@mail.com" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<EmailTestResult>())!;
        result.Ok.Should().BeFalse();
        result.Configured.Should().BeFalse();
        result.Missing.Should().Contain(new[] { "Email:Smtp:User", "Email:Smtp:Password" });
    }

    [Fact]
    public async Task TheTestEmail_IsForAdminsOnly_AndNeedsAnAddress()
    {
        (await Send(HttpMethod.Post, "/api/admin/email-test", await UserToken(), new { to = "me@mail.com" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Post, "/api/admin/email-test", await AdminToken(), new { to = "nope" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
