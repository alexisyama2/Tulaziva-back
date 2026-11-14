using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tulaziva.Application.Auth;
using Tulaziva.Domain.Entities;
using Tulaziva.Infrastructure.Email;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Email;

public class EmailDeliveryTests
{
    private static IOptions<EmailOptions> Options(FakeSmtpServer? server = null, string user = "no-reply@fototrampa.com", string from = "", params int[] delays) =>
        Microsoft.Extensions.Options.Options.Create(new EmailOptions
        {
            From = from,
            RetryDelaysSeconds = delays.Length == 0 ? new[] { 0, 0 } : delays,
            Smtp = new SmtpOptions
            {
                Host = "127.0.0.1",
                Port = server?.Port ?? 25,
                EnableSsl = false, // the fake server has no TLS; the real one is reached with STARTTLS
                User = user,
                Password = "s3cret",
                TimeoutSeconds = 5
            }
        });

    private static EmailMessage Message(string html = "<p>Hola</p>") => new("ana@mail.com", "Verifica tu correo — ¡Bienvenida!", "Hola Ana", html);

    // ---- The settings ----

    [Fact]
    public void TheSender_IsTheFromAddress_WithItsName()
    {
        var from = new EmailOptions { From = "Tulaziva <hola@fototrampa.com>" }.ResolveFrom();

        from.Address.Should().Be("hola@fototrampa.com");
        from.DisplayName.Should().Be("Tulaziva");
    }

    [Fact]
    public void WithoutAFromAddress_TheSmtpUserIsTheSender()
    {
        var from = new EmailOptions { From = "", FromName = "Tulaziva", Smtp = new SmtpOptions { User = "no-reply@fototrampa.com" } }.ResolveFrom();

        from.Address.Should().Be("no-reply@fototrampa.com");
        from.DisplayName.Should().Be("Tulaziva");
    }

    [Fact]
    public void WithoutAnyAddress_ItFailsWithAClearMessage()
    {
        var act = () => new EmailOptions { From = "not an address", Smtp = new SmtpOptions { User = "admin" } }.ResolveFrom();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Email:From*");
    }

    [Theory]
    [InlineData("mail5011.site4now.net", "no-reply@fototrampa.com", "s3cret", true)]
    [InlineData("mail5011.site4now.net", "no-reply@fototrampa.com", "", false)]
    [InlineData("mail5011.site4now.net", "", "s3cret", false)]
    [InlineData("", "no-reply@fototrampa.com", "s3cret", false)]
    public void SmtpIsUsedOnlyWithAServerAndTheWholeAccount(string host, string user, string password, bool expected)
    {
        new SmtpOptions { Host = host, User = user, Password = password }.IsConfigured.Should().Be(expected);
    }

    [Fact]
    public void TheConfiguredRetryDelays_ReplaceTheDefault_InsteadOfBeingAddedToIt()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:RetryDelaysSeconds:0"] = "2", ["Email:RetryDelaysSeconds:1"] = "10" })
            .Build();

        var options = Microsoft.Extensions.Configuration.ConfigurationBinder.Get<EmailOptions>(config.GetSection("Email"))!;

        options.RetryDelays.Should().Equal(2, 10);
        new EmailOptions().RetryDelays.Should().Equal(2, 10); // and without configuration, the default
    }

    // ---- The HTML of the emails ----

    private async Task<EmailMessage> VerificationFor(string displayName, string language)
    {
        var email = new CapturingEmailSender();
        var service = new AccountEmailService(new FakeUserTokenRepository(), email, new StaticAppLinks(), TimeProvider.System);

        await service.SendVerificationAsync(new User { Email = "ana@mail.com", DisplayName = displayName, PreferredLanguage = language }, CancellationToken.None);

        return email.Sent.Single();
    }

    [Fact]
    public async Task TheVerificationEmail_HasTheTextAndAnHtmlVersionWithTheSameLinkAsAButton()
    {
        var message = await VerificationFor("Ana", "es");

        var link = message.Body.Split('\n').Single(l => l.StartsWith("https://app.test/verify-email?token="));
        message.HtmlBody.Should().NotBeNull();
        message.HtmlBody.Should().Contain($"href=\"{link}\"").And.Contain("Verificar mi correo");
    }

    [Fact]
    public async Task TheHtmlVersion_EscapesWhatTheUserTyped()
    {
        var message = await VerificationFor("<script>alert(1)</script> & Co", "en");

        message.HtmlBody.Should().NotContain("<script>").And.Contain("&lt;script&gt;").And.Contain("&amp; Co");
        message.HtmlBody.Should().Contain("Verify my email");
    }

    [Fact]
    public async Task TheNoticeOfAChangedEmail_HasNoButton()
    {
        var email = new CapturingEmailSender();
        var service = new AccountEmailService(new FakeUserTokenRepository(), email, new StaticAppLinks(), TimeProvider.System);

        await service.SendEmailChangedNoticeAsync(new User { Email = "new@mail.com", DisplayName = "Ana", PreferredLanguage = "es" }, "old@mail.com", CancellationToken.None);

        var message = email.Sent.Single();
        message.To.Should().Be("old@mail.com");
        message.HtmlBody.Should().Contain("new@mail.com").And.NotContain("<a href");
    }

    // ---- The transport, against a real SMTP conversation ----

    [Fact]
    public async Task TheTransport_SendsAMultipartMessage_SignedInWithTheAccount()
    {
        using var server = new FakeSmtpServer();
        var transport = new SmtpEmailTransport(Options(server));

        await transport.DeliverAsync(Message(), CancellationToken.None);

        var mail = server.Received.Should().ContainSingle().Subject;
        mail.User.Should().Be("no-reply@fototrampa.com");
        mail.Password.Should().Be("s3cret");
        mail.From.Should().Contain("no-reply@fototrampa.com");
        mail.To.Should().Contain("ana@mail.com");
        mail.Data.Should().Contain("multipart/alternative").And.Contain("text/plain").And.Contain("text/html");
        mail.Data.Should().Contain("Auto-Submitted: auto-generated");
        mail.Data.Should().Contain("From: \"Tulaziva\" <no-reply@fototrampa.com>");
        mail.Data.Should().Contain("Subject: =?utf-8?"); // the accents travel encoded
    }

    [Fact]
    public async Task TheTransport_WithoutHtml_SendsPlainText()
    {
        using var server = new FakeSmtpServer();

        await new SmtpEmailTransport(Options(server)).DeliverAsync(new EmailMessage("ana@mail.com", "Hola", "Solo texto"), CancellationToken.None);

        var data = server.Received.Single().Data;
        data.Should().Contain("text/plain").And.NotContain("multipart/alternative").And.NotContain("text/html");
    }

    [Fact]
    public async Task TheTransport_WhenTheServerRefuses_Throws()
    {
        using var server = new FakeSmtpServer(refuseFirstSessions: 1);

        var act = () => new SmtpEmailTransport(Options(server)).DeliverAsync(Message(), CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
    }

    // ---- The queue, the retries and the log ----

    private sealed class FlakyTransport : IEmailTransport
    {
        private readonly int _failures;

        public FlakyTransport(int failures) => _failures = failures;

        public int Attempts { get; private set; }

        public List<EmailMessage> Delivered { get; } = new();

        public Task DeliverAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Attempts++;

            if (Attempts <= _failures)
                throw new IOException("server down");

            Delivered.Add(message);
            return Task.CompletedTask;
        }
    }

    private static EmailDispatcherService Dispatcher(IEmailTransport transport, FakeEmailLogWriter? log = null, Channel<QueuedEmail>? queue = null) =>
        new(queue ?? Channel.CreateUnbounded<QueuedEmail>(), transport, log ?? new FakeEmailLogWriter(), Options(), NullLogger<EmailDispatcherService>.Instance);

    [Fact]
    public async Task ASendThatWorks_IsDeliveredOnce_AndLoggedAsSent()
    {
        var transport = new FlakyTransport(failures: 0);
        var log = new FakeEmailLogWriter();
        var id = Guid.NewGuid();

        await Dispatcher(transport, log).DeliverWithRetriesAsync(Message(), id, CancellationToken.None);

        transport.Attempts.Should().Be(1);
        transport.Delivered.Should().ContainSingle();
        log.Sent.Should().ContainSingle().Which.Should().Be((id, 1));
        log.Failed.Should().BeEmpty();
    }

    [Fact]
    public async Task AFailure_IsTriedAgain_UntilItWorks_AndEachFailureIsLogged()
    {
        var transport = new FlakyTransport(failures: 2);
        var log = new FakeEmailLogWriter();
        var id = Guid.NewGuid();

        await Dispatcher(transport, log).DeliverWithRetriesAsync(Message(), id, CancellationToken.None);

        transport.Attempts.Should().Be(3);
        transport.Delivered.Should().ContainSingle();
        log.AttemptsFailed.Select(a => a.Attempts).Should().Equal(1, 2);
        log.AttemptsFailed.Should().OnlyContain(a => a.Error.Contains("IOException") && a.Error.Contains("server down"));
        log.Sent.Should().ContainSingle().Which.Should().Be((id, 3));
    }

    [Fact]
    public async Task WhenEveryAttemptFails_ItGivesUp_WithoutThrowing_AndLogsWhy()
    {
        var transport = new FlakyTransport(failures: 99);
        var log = new FakeEmailLogWriter();
        var id = Guid.NewGuid();

        var act = () => Dispatcher(transport, log).DeliverWithRetriesAsync(Message(), id, CancellationToken.None);

        await act.Should().NotThrowAsync();
        transport.Attempts.Should().Be(3); // the first one and one more after each delay (0 and 0)
        transport.Delivered.Should().BeEmpty();
        var failed = log.Failed.Should().ContainSingle().Subject;
        failed.Id.Should().Be(id);
        failed.Attempts.Should().Be(3);
        failed.Error.Should().Contain("server down");
        log.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task TheQueuedSender_LogsTheEmail_ReturnsAtOnce_AndTheServiceSendsInTheBackground()
    {
        var queue = Channel.CreateBounded<QueuedEmail>(10);
        var transport = new FlakyTransport(failures: 1);
        var log = new FakeEmailLogWriter();
        var dispatcher = Dispatcher(transport, log, queue);

        await new QueuedEmailSender(queue, log, NullLogger<QueuedEmailSender>.Instance).SendAsync(Message(), CancellationToken.None);
        transport.Attempts.Should().Be(0); // nothing is sent while the caller waits
        log.Recorded.Should().ContainSingle().Which.Status.Should().Be(Tulaziva.Domain.Enums.EmailStatus.Queued);

        await dispatcher.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (log.Sent.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        await dispatcher.StopAsync(CancellationToken.None);

        transport.Delivered.Should().ContainSingle();
        transport.Attempts.Should().Be(2);
        log.Sent.Should().ContainSingle().Which.Id.Should().Be(log.Recorded.Single().Id); // the row that was created is the one updated
    }

    [Fact]
    public async Task AFullQueue_DropsTheEmail_LogsItAsFailed_AndDoesNotFailTheCaller()
    {
        var queue = Channel.CreateBounded<QueuedEmail>(1);
        var log = new FakeEmailLogWriter();
        var sender = new QueuedEmailSender(queue, log, NullLogger<QueuedEmailSender>.Instance);

        await sender.SendAsync(Message(), CancellationToken.None);
        var act = () => sender.SendAsync(Message(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        queue.Reader.Count.Should().Be(1);
        log.Failed.Should().ContainSingle().Which.Error.Should().Contain("queue was full");
    }

    private sealed class ThrowingLogWriter : IEmailLogWriter
    {
        public Task<Guid> RecordAsync(EmailMessage message, Tulaziva.Domain.Enums.EmailStatus status) => throw new InvalidOperationException("database down");

        public Task RecordAttemptFailedAsync(Guid id, int attempts, string error) => throw new InvalidOperationException("database down");

        public Task RecordSentAsync(Guid id, int attempts) => throw new InvalidOperationException("database down");

        public Task RecordFailedAsync(Guid id, int attempts, string error) => throw new InvalidOperationException("database down");
    }

    [Fact]
    public async Task IfTheLogItselfBreaks_TheSignUpStillWorks()
    {
        var sender = new QueuedEmailSender(Channel.CreateBounded<QueuedEmail>(1), new ThrowingLogWriter(), NullLogger<QueuedEmailSender>.Instance);

        var act = () => sender.SendAsync(Message(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EndToEnd_ARefusedFirstConnection_IsRetried_AndTheEmailArrives()
    {
        using var server = new FakeSmtpServer(refuseFirstSessions: 1);
        var log = new FakeEmailLogWriter();
        var dispatcher = new EmailDispatcherService(
            Channel.CreateUnbounded<QueuedEmail>(), new SmtpEmailTransport(Options(server)), log, Options(server), NullLogger<EmailDispatcherService>.Instance);

        await dispatcher.DeliverWithRetriesAsync(Message(), Guid.NewGuid(), CancellationToken.None);

        server.Sessions.Should().BeGreaterThanOrEqualTo(2);
        server.Received.Should().ContainSingle();
        log.AttemptsFailed.Should().ContainSingle().Which.Error.Should().Contain("ServiceNotAvailable").And.Contain("Service busy");
        log.Sent.Should().ContainSingle().Which.Attempts.Should().Be(2);
    }

    // ---- What goes in the log ----

    [Fact]
    public void TheErrorText_NamesEveryCause_AndTheSmtpStatus()
    {
        var error = new System.Net.Mail.SmtpException("Authentication failed", new IOException("connection reset")) { };

        var text = EmailErrors.Describe(new InvalidOperationException("could not send", error));

        text.Should().Contain("InvalidOperationException: could not send")
            .And.Contain("SmtpException: Authentication failed (status GeneralFailure)")
            .And.Contain("IOException: connection reset");
    }

    private static (ServiceProvider Provider, EmailLogWriter Writer) LogWriter()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContext<Tulaziva.Infrastructure.Persistence.TulazivaDbContext>(o => Microsoft.EntityFrameworkCore.InMemoryDbContextOptionsExtensions.UseInMemoryDatabase(o, name));
        var provider = services.BuildServiceProvider();

        return (provider, new EmailLogWriter(provider.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NullLogger<EmailLogWriter>.Instance));
    }

    [Fact]
    public async Task TheLog_FollowsAnEmailFromQueuedToFailedToSent_WithoutKeepingItsText()
    {
        var (provider, writer) = LogWriter();
        using var _ = provider;
        var id = await writer.RecordAsync(Message(), Tulaziva.Domain.Enums.EmailStatus.Queued);
        id.Should().NotBe(Guid.Empty);

        await writer.RecordAttemptFailedAsync(id, 1, "boom");
        await writer.RecordSentAsync(id, 2);

        using var scope = provider.CreateScope();
        var row = scope.ServiceProvider.GetRequiredService<Tulaziva.Infrastructure.Persistence.TulazivaDbContext>().EmailLogs.Single();
        row.ToAddress.Should().Be("ana@mail.com");
        row.Status.Should().Be(Tulaziva.Domain.Enums.EmailStatus.Sent);
        row.Attempts.Should().Be(2);
        row.LastError.Should().BeNull();
        row.SentAtUtc.Should().NotBeNull();
        System.Text.Json.JsonSerializer.Serialize(row).Should().NotContain("Hola Ana"); // no body in the log
    }

    [Fact]
    public async Task TheLog_KeepsTheReasonOfAFinalFailure_AndCutsLongErrors()
    {
        var (provider, writer) = LogWriter();
        using var _ = provider;
        var id = await writer.RecordAsync(Message(), Tulaziva.Domain.Enums.EmailStatus.Queued);

        await writer.RecordFailedAsync(id, 3, new string('x', 5000));

        using var scope = provider.CreateScope();
        var row = scope.ServiceProvider.GetRequiredService<Tulaziva.Infrastructure.Persistence.TulazivaDbContext>().EmailLogs.Single();
        row.Status.Should().Be(Tulaziva.Domain.Enums.EmailStatus.Failed);
        row.LastError!.Length.Should().Be(2000);
    }

    [Fact]
    public async Task UpdatingARowThatDoesNotExist_IsHarmless()
    {
        var (provider, writer) = LogWriter();
        using var _ = provider;

        var act = async () =>
        {
            await writer.RecordSentAsync(Guid.NewGuid(), 1);
            await writer.RecordSentAsync(Guid.Empty, 1);
        };

        await act.Should().NotThrowAsync();
    }
}

public class EmailDiagnosticsTests
{
    private sealed class FailingTransport : IEmailTransport
    {
        private readonly Exception _error;

        public FailingTransport(Exception error) => _error = error;

        public Task DeliverAsync(EmailMessage message, CancellationToken cancellationToken) => throw _error;
    }

    private static Microsoft.Extensions.Options.IOptions<EmailOptions> Options(string password = "s3cret") =>
        Microsoft.Extensions.Options.Options.Create(new EmailOptions { Smtp = new SmtpOptions { Host = "smtp.test", User = "me@fototrampa.com", Password = password } });

    private static EmailDiagnostics Diagnostics(IEmailTransport? transport, FakeEmailLogWriter log, Microsoft.Extensions.Options.IOptions<EmailOptions>? options = null) =>
        new(options ?? Options(), log, NullLogger<EmailDiagnostics>.Instance, transport);

    [Fact]
    public async Task WhenTheServerRefusesAnOutsideAddress_ItSaysSoAndHintsAtTheAccount()
    {
        var log = new FakeEmailLogWriter();
        var error = new System.Net.Mail.SmtpFailedRecipientException(System.Net.Mail.SmtpStatusCode.MailboxUnavailable, "who@gmail.com", "Mailbox unavailable. <who@gmail.com> No such user here");

        var result = await Diagnostics(new FailingTransport(error), log).SendTestAsync("who@gmail.com", CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Configured.Should().BeTrue();
        result.Error.Should().Contain("No such user here");
        result.Hint.Should().Contain("Email:Smtp:Password");
        log.Failed.Should().ContainSingle().Which.Error.Should().Contain("No such user here"); // and it is in the log table too
    }

    [Fact]
    public async Task WhenTheServerCannotBeReached_TheHintSaysToCheckHostAndPort()
    {
        var result = await Diagnostics(new FailingTransport(new System.Net.Mail.SmtpException("Unable to connect to the remote server")), new FakeEmailLogWriter())
            .SendTestAsync("who@mail.com", CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Hint.Should().Contain("Email:Smtp:Host");
    }

    [Fact]
    public async Task WhenItWorks_ItIsLoggedAsSent()
    {
        var log = new FakeEmailLogWriter();

        var result = await Diagnostics(new FlakyForDiagnostics(), log).SendTestAsync("who@mail.com", CancellationToken.None);

        result.Ok.Should().BeTrue();
        result.Sender.Should().Be("me@fototrampa.com");
        log.Sent.Should().ContainSingle();
    }

    private sealed class FlakyForDiagnostics : IEmailTransport
    {
        public Task DeliverAsync(EmailMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task WithoutTheAccount_ItNamesTheMissingSettings_AndSendsNothing()
    {
        var log = new FakeEmailLogWriter();

        var result = await Diagnostics(null, log, Options(password: "")).SendTestAsync("who@mail.com", CancellationToken.None);

        result.Configured.Should().BeFalse();
        result.Missing.Should().Equal("Email:Smtp:Password");
        log.Recorded.Should().BeEmpty();
    }
}
