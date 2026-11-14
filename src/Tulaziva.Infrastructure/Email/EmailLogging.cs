using System.Net.Mail;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tulaziva.Application.Admin;
using Tulaziva.Application.Auth;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;
using Tulaziva.Infrastructure.Persistence;

namespace Tulaziva.Infrastructure.Email;

/// <summary>
/// Writes the email log. Every method catches its own failures: a problem with the log (for example the database being down) must never
/// stop an email from being sent or turn a sign-up into an error.
/// </summary>
public interface IEmailLogWriter
{
    /// <summary>Records a new email and returns the id of its row, or <see cref="Guid.Empty"/> when it could not be recorded.</summary>
    Task<Guid> RecordAsync(EmailMessage message, EmailStatus status);

    Task RecordAttemptFailedAsync(Guid id, int attempts, string error);

    Task RecordSentAsync(Guid id, int attempts);

    Task RecordFailedAsync(Guid id, int attempts, string error);
}

public sealed class EmailLogWriter : IEmailLogWriter
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EmailLogWriter> _logger;

    public EmailLogWriter(IServiceScopeFactory scopes, ILogger<EmailLogWriter> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task<Guid> RecordAsync(EmailMessage message, EmailStatus status)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TulazivaDbContext>();

            var log = new EmailLog
            {
                ToAddress = Cut(message.To, 256),
                Subject = Cut(message.Subject, 200),
                Status = status,
                CreatedAtUtc = DateTime.UtcNow
            };

            db.EmailLogs.Add(log);
            await db.SaveChangesAsync();

            return log.Id;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The email to {To} could not be recorded in the email log", message.To);
            return Guid.Empty;
        }
    }

    public Task RecordAttemptFailedAsync(Guid id, int attempts, string error) =>
        UpdateAsync(id, log =>
        {
            log.Attempts = attempts;
            log.LastError = Cut(error, 2000);
            log.LastAttemptAtUtc = DateTime.UtcNow;
        });

    public Task RecordSentAsync(Guid id, int attempts) =>
        UpdateAsync(id, log =>
        {
            var now = DateTime.UtcNow;
            log.Status = EmailStatus.Sent;
            log.Attempts = attempts;
            log.LastAttemptAtUtc = now;
            log.SentAtUtc = now;
            log.LastError = null;
        });

    public Task RecordFailedAsync(Guid id, int attempts, string error) =>
        UpdateAsync(id, log =>
        {
            log.Status = EmailStatus.Failed;
            log.Attempts = attempts;
            log.LastError = Cut(error, 2000);
            log.LastAttemptAtUtc = DateTime.UtcNow;
        });

    private async Task UpdateAsync(Guid id, Action<EmailLog> change)
    {
        if (id == Guid.Empty)
            return;

        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TulazivaDbContext>();
            var log = await db.EmailLogs.FirstOrDefaultAsync(l => l.Id == id);

            if (log == null)
                return;

            change(log);
            await db.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The email log row {Id} could not be updated", id);
        }
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];
}

/// <summary>Turns an exception into the text that goes to the email log: what failed, with the SMTP status code and the causes.</summary>
public static class EmailErrors
{
    /// <summary>What to check for the errors mail servers give most often, or null when there is nothing specific to say.</summary>
    public static string? Hint(string error)
    {
        // A server that does not accept the account does not say so: it treats the send as not signed in, and then refuses (or does not know)
        // every recipient that is not one of its own mailboxes. So a wrong password only shows with outside addresses.
        if (error.Contains("No such user here", StringComparison.OrdinalIgnoreCase)
            || error.Contains("relay", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Authentication", StringComparison.OrdinalIgnoreCase)
            || error.Contains("5.7.", StringComparison.Ordinal)
            || error.Contains("MailboxBusy", StringComparison.Ordinal) && error.Contains("auth", StringComparison.OrdinalIgnoreCase))
            return "The mail server did not accept the account (or treated the send as not signed in). Check Email:Smtp:User and Email:Smtp:Password. A wrong password only shows with recipients outside the server own domain.";

        if (error.Contains("timed out", StringComparison.OrdinalIgnoreCase) || error.Contains("Unable to connect", StringComparison.OrdinalIgnoreCase) || error.Contains("actively refused", StringComparison.OrdinalIgnoreCase))
            return "The mail server could not be reached: check Email:Smtp:Host and Port (587 with TLS) and that the network or firewall allows the connection.";

        return null;
    }

    public static string Describe(Exception exception)
    {
        var text = new StringBuilder();

        for (var current = exception; current != null; current = current.InnerException)
        {
            if (text.Length > 0)
                text.Append(" <- ");

            text.Append(current.GetType().Name).Append(": ").Append(current.Message);

            if (current is SmtpFailedRecipientException recipient)
                text.Append(" (recipient ").Append(recipient.FailedRecipient).Append(", status ").Append(recipient.StatusCode).Append(')');
            else if (current is SmtpException smtp)
                text.Append(" (status ").Append(smtp.StatusCode).Append(')');
        }

        return text.ToString();
    }
}

public sealed class EmailLogRepository : IEmailLogRepository
{
    private readonly TulazivaDbContext _db;

    public EmailLogRepository(TulazivaDbContext db)
    {
        _db = db;
    }

    public async Task<(IReadOnlyList<EmailLog> Items, int TotalCount)> GetPageAsync(EmailStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _db.EmailLogs.AsNoTracking();

        if (status.HasValue)
            query = query.Where(l => l.Status == status.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(l => l.CreatedAtUtc)
            .ThenBy(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}

/// <summary>Sends a test email straight to the mail server and says exactly what went wrong, so the account can be set up without guessing.</summary>
public sealed class EmailDiagnostics : IEmailDiagnostics
{
    private readonly EmailOptions _options;
    private readonly IEmailTransport? _transport;
    private readonly IEmailLogWriter _log;
    private readonly ILogger<EmailDiagnostics> _logger;

    public EmailDiagnostics(IOptions<EmailOptions> options, IEmailLogWriter log, ILogger<EmailDiagnostics> logger, IEmailTransport? transport = null)
    {
        _options = options.Value;
        _transport = transport;
        _log = log;
        _logger = logger;
    }

    public async Task<EmailTestResult> SendTestAsync(string to, CancellationToken cancellationToken)
    {
        var smtp = _options.Smtp;
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(smtp.Host)) missing.Add("Email:Smtp:Host");
        if (string.IsNullOrWhiteSpace(smtp.User)) missing.Add("Email:Smtp:User");
        if (string.IsNullOrWhiteSpace(smtp.Password)) missing.Add("Email:Smtp:Password");

        if (_transport == null || missing.Count > 0)
            return new EmailTestResult(false, false, missing.ToArray(), null, "SMTP is not configured: the emails are only written to the application log.");

        var message = new EmailMessage(
            to,
            "Tulaziva: prueba de correo / email test",
            "Si recibes este correo, el envío desde Tulaziva funciona.\n\nIf you receive this email, sending from Tulaziva works.\n\nTulaziva");

        string? sender = null;
        var id = await _log.RecordAsync(message, EmailStatus.Queued);

        try
        {
            sender = _options.ResolveFrom().Address;
            await _transport.DeliverAsync(message, cancellationToken);
            await _log.RecordSentAsync(id, 1);

            return new EmailTestResult(true, true, Array.Empty<string>(), sender, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var error = EmailErrors.Describe(exception);
            _logger.LogError(exception, "The test email to {To} failed", to);
            await _log.RecordFailedAsync(id, 1, error);

            return new EmailTestResult(false, true, Array.Empty<string>(), sender, error, EmailErrors.Hint(error));
        }
    }
}
