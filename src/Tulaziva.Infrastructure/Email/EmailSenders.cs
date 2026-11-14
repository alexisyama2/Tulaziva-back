using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tulaziva.Application.Auth;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Infrastructure.Email;

public sealed class EmailOptions
{
    /// <summary>The address of the web app, used to build the links of the emails.</summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";

    /// <summary>
    /// The "from" of the emails, as <c>Name &lt;address@domain&gt;</c> or just the address. Many mail servers only accept the address of the
    /// mailbox that signs in, so when this is empty (or not an address) the SMTP user is used.
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>The name shown next to the address when <see cref="From"/> is not used.</summary>
    public string FromName { get; set; } = "Tulaziva";

    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>
    /// Seconds to wait before each new attempt when sending fails; its length is the number of retries. Null means the default.
    /// It has no default value in the property on purpose: the configuration binder would add the configured values to it instead of replacing it.
    /// </summary>
    public int[]? RetryDelaysSeconds { get; set; }

    public static readonly int[] DefaultRetryDelaysSeconds = { 2, 10 };

    public int[] RetryDelays => RetryDelaysSeconds ?? DefaultRetryDelaysSeconds;

    /// <summary>The sender of the emails. Throws with a clear message when neither <see cref="From"/> nor the SMTP user is an address.</summary>
    public MailAddress ResolveFrom()
    {
        if (!string.IsNullOrWhiteSpace(From) && MailAddress.TryCreate(From, out var from))
            return from;

        if (!string.IsNullOrWhiteSpace(Smtp.User) && MailAddress.TryCreate(Smtp.User, FromName, out var user))
            return user;

        throw new InvalidOperationException(
            "Email:From is not a valid address and Email:Smtp:User is not an address either, so the sender of the emails is unknown.");
    }
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;

    /// <summary>587 (STARTTLS) is the one that works here. Port 465 (implicit SSL) is not supported by the .NET SMTP client.</summary>
    public int Port { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>How long a send may take before it is given up (and tried again).</summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Emails are only sent when there is a server and the account to sign in to (user and password); otherwise they go to the log.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(Password);
}

/// <summary>Builds the links of the emails from the address of the web app.</summary>
public sealed class AppLinks : IAppLinks
{
    private readonly string _baseUrl;

    public AppLinks(IOptions<EmailOptions> options)
    {
        _baseUrl = options.Value.FrontendBaseUrl.TrimEnd('/');
    }

    public string PasswordReset(string token) => $"{_baseUrl}/reset-password?token={Uri.EscapeDataString(token)}";

    public string VerifyEmail(string token) => $"{_baseUrl}/verify-email?token={Uri.EscapeDataString(token)}";

    public string ConfirmEmailChange(string token) => $"{_baseUrl}/confirm-email-change?token={Uri.EscapeDataString(token)}";
}

/// <summary>Development sender: nothing leaves the machine, the email (and its links) go to the log.</summary>
public sealed class LogEmailSender : IEmailSender
{
    private readonly ILogger<LogEmailSender> _logger;
    private readonly IEmailLogWriter _log;

    public LogEmailSender(ILogger<LogEmailSender> logger, IEmailLogWriter log, IOptions<EmailOptions> options)
    {
        _logger = logger;
        _log = log;

        var smtp = options.Value.Smtp;
        var missing = new[]
            {
                string.IsNullOrWhiteSpace(smtp.Host) ? "Email:Smtp:Host" : null,
                string.IsNullOrWhiteSpace(smtp.User) ? "Email:Smtp:User" : null,
                string.IsNullOrWhiteSpace(smtp.Password) ? "Email:Smtp:Password" : null
            }
            .Where(x => x != null);

        _logger.LogWarning("SMTP is not configured (missing: {Missing}): the emails are written to this log instead of being sent.", string.Join(", ", missing));
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("EMAIL to {To} | {Subject}\n{Body}", message.To, message.Subject, message.Body);
            await _log.RecordAsync(message, EmailStatus.Logged);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The email to {To} could not be logged", message.To);
        }
    }
}

/// <summary>Delivers one email to the mail server. It throws when that fails; the caller decides about retrying.</summary>
public interface IEmailTransport
{
    Task DeliverAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Sends through an SMTP server. A multipart message: the plain text and, when there is one, the HTML version.</summary>
public sealed class SmtpEmailTransport : IEmailTransport
{
    private readonly EmailOptions _options;

    public SmtpEmailTransport(IOptions<EmailOptions> options)
    {
        _options = options.Value;
    }

    public async Task DeliverAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var smtp = _options.Smtp;

        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = Math.Max(1, smtp.TimeoutSeconds) * 1000
        };

        if (!string.IsNullOrEmpty(smtp.User))
            client.Credentials = new NetworkCredential(smtp.User, smtp.Password);

        using var mail = new MailMessage
        {
            From = _options.ResolveFrom(),
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = message.Body,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        mail.To.Add(message.To);

        // Tells mail servers and auto-responders that no person wrote this (no "out of office" replies to it).
        mail.Headers.Add("Auto-Submitted", "auto-generated");

        if (message.HtmlBody != null)
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));

        await client.SendMailAsync(mail, cancellationToken);
    }
}

/// <summary>
/// What the rest of the app uses to send email: it only puts the message in a queue and returns, so a slow or unreachable mail server
/// never makes a sign-up wait. <see cref="EmailDispatcherService"/> does the sending.
/// </summary>
public sealed class QueuedEmailSender : IEmailSender
{
    private readonly Channel<QueuedEmail> _queue;
    private readonly IEmailLogWriter _log;
    private readonly ILogger<QueuedEmailSender> _logger;

    public QueuedEmailSender(Channel<QueuedEmail> queue, IEmailLogWriter log, ILogger<QueuedEmailSender> logger)
    {
        _queue = queue;
        _log = log;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var id = await _log.RecordAsync(message, EmailStatus.Queued);

            // The queue is bounded: if the mail server has been down for long enough to fill it, the newest emails are dropped, not the app.
            if (!_queue.Writer.TryWrite(new QueuedEmail(message, id)))
            {
                _logger.LogError("The email queue is full: the email to {To} was dropped", message.To);
                await _log.RecordFailedAsync(id, 0, "The email queue was full, so the email was dropped before any attempt.");
            }
        }
        catch (Exception exception)
        {
            // Sending an email is best effort (see IEmailSender): whatever goes wrong here must not break the sign-up.
            _logger.LogError(exception, "The email to {To} could not be queued", message.To);
        }
    }
}

/// <summary>An email waiting in the queue, with the id of its row in the email log.</summary>
public sealed record QueuedEmail(EmailMessage Message, Guid LogId);

/// <summary>Takes the emails out of the queue one by one and sends them, trying again when the server fails.</summary>
public sealed class EmailDispatcherService : BackgroundService
{
    private readonly Channel<QueuedEmail> _queue;
    private readonly IEmailTransport _transport;
    private readonly IEmailLogWriter _log;
    private readonly EmailOptions _options;
    private readonly ILogger<EmailDispatcherService> _logger;

    public EmailDispatcherService(
        Channel<QueuedEmail> queue, IEmailTransport transport, IEmailLogWriter log, IOptions<EmailOptions> options, ILogger<EmailDispatcherService> logger)
    {
        _queue = queue;
        _transport = transport;
        _log = log;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var smtp = _options.Smtp;
        _logger.LogInformation("Email dispatcher started: SMTP {Host}:{Port} as {User}, TLS {Tls}, {Retries} retries", smtp.Host, smtp.Port, smtp.User, smtp.EnableSsl, _options.RetryDelays.Length);

        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await DeliverWithRetriesAsync(item.Message, item.LogId, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Nothing that goes wrong with one email may stop the service: the next ones still have to be sent.
                    _logger.LogError(exception, "Unexpected error while sending the email to {To}", item.Message.To);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The app is stopping.
        }
    }

    /// <summary>One attempt, plus one more after each delay in <see cref="EmailOptions.RetryDelaysSeconds"/>. Never throws: a failure is logged.</summary>
    public async Task DeliverWithRetriesAsync(EmailMessage message, Guid logId, CancellationToken cancellationToken)
    {
        var delays = _options.RetryDelays;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await _transport.DeliverAsync(message, cancellationToken);
                _logger.LogInformation("Email sent to {To} ({Subject}), attempt {Attempt}", message.To, message.Subject, attempt + 1);
                await _log.RecordSentAsync(logId, attempt + 1);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                var error = EmailErrors.Describe(exception);

                if (attempt >= delays.Length)
                {
                    _logger.LogError(exception, "The email to {To} ({Subject}) could not be sent after {Attempts} attempts: {Error}", message.To, message.Subject, attempt + 1, error);
                    await _log.RecordFailedAsync(logId, attempt + 1, error);
                    return;
                }

                _logger.LogWarning(exception, "Sending the email to {To} failed (attempt {Attempt}); trying again in {Delay} s: {Error}", message.To, attempt + 1, delays[attempt], error);
                await _log.RecordAttemptFailedAsync(logId, attempt + 1, error);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, delays[attempt])), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
