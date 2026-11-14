using FluentValidation;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Application.Admin;

public interface IEmailLogRepository
{
    /// <summary>The email log, newest first, optionally of one status. Page is 1-based.</summary>
    Task<(IReadOnlyList<EmailLog> Items, int TotalCount)> GetPageAsync(EmailStatus? status, int page, int pageSize, CancellationToken cancellationToken);
}

public sealed record EmailLogDto(
    Guid Id,
    string To,
    string Subject,
    string Status,
    int Attempts,
    string? LastError,
    DateTime CreatedAtUtc,
    DateTime? LastAttemptAtUtc,
    DateTime? SentAtUtc);

// ---- The log ----

/// <summary>The emails the application tried to send and what happened to them, so a failing mail server can be diagnosed.</summary>
public sealed record GetEmailLogsQuery(EmailStatus? Status = null, int Page = 1, int PageSize = 20) : IQuery<PagedResult<EmailLogDto>>;

public sealed class GetEmailLogsQueryValidator : AbstractValidator<GetEmailLogsQuery>
{
    public const int MaxPageSize = 100;

    public GetEmailLogsQueryValidator(ILocalizer localizer)
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage(_ => localizer.Get(MessageKeys.PageInvalid));
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize).WithMessage(_ => localizer.Get(MessageKeys.PageSizeInvalid, MaxPageSize));
    }
}

public sealed class GetEmailLogsQueryHandler : IQueryHandler<GetEmailLogsQuery, PagedResult<EmailLogDto>>
{
    private readonly IEmailLogRepository _logs;

    public GetEmailLogsQueryHandler(IEmailLogRepository logs)
    {
        _logs = logs;
    }

    public async Task<PagedResult<EmailLogDto>> HandleAsync(GetEmailLogsQuery query, CancellationToken cancellationToken)
    {
        var (items, total) = await _logs.GetPageAsync(query.Status, query.Page, query.PageSize, cancellationToken);

        return new PagedResult<EmailLogDto>(
            items.Select(l => new EmailLogDto(l.Id, l.ToAddress, l.Subject, l.Status.ToString(), l.Attempts, l.LastError, l.CreatedAtUtc, l.LastAttemptAtUtc, l.SentAtUtc)).ToList(),
            query.Page,
            query.PageSize,
            total);
    }
}

// ---- A test email ----

/// <summary>What happened when a test email was sent straight to the mail server (not through the queue).</summary>
public sealed record EmailTestResult(bool Ok, bool Configured, string[] Missing, string? Sender, string? Error, string? Hint = null);

public interface IEmailDiagnostics
{
    /// <summary>Sends a test email now, waiting for the mail server, and reports the real error when it fails.</summary>
    Task<EmailTestResult> SendTestAsync(string to, CancellationToken cancellationToken);
}

/// <summary>Sends a test email and answers at once with what the mail server said. For an admin who is setting up the mail account.</summary>
public sealed record SendTestEmailCommand(string To) : ICommand<EmailTestResult>;

public sealed class SendTestEmailCommandValidator : AbstractValidator<SendTestEmailCommand>
{
    public SendTestEmailCommandValidator(ILocalizer localizer)
    {
        RuleFor(x => x.To).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.EmailRequired))
            .EmailAddress().WithMessage(_ => localizer.Get(MessageKeys.EmailInvalid));
    }
}

public sealed class SendTestEmailCommandHandler : ICommandHandler<SendTestEmailCommand, EmailTestResult>
{
    private readonly IEmailDiagnostics _diagnostics;

    public SendTestEmailCommandHandler(IEmailDiagnostics diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public Task<EmailTestResult> HandleAsync(SendTestEmailCommand command, CancellationToken cancellationToken) =>
        _diagnostics.SendTestAsync(command.To.Trim(), cancellationToken);
}
