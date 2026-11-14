using FluentValidation;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Application.Reports;

public static class ReportRules
{
    public const int MaxDetailsLength = 1000;

    /// <summary>The reasons as the API names them (camelCase), in the order of the enum.</summary>
    public static readonly IReadOnlyList<string> Reasons = Enum.GetNames<ReportReason>().Select(ToApiName).ToList();

    public static readonly IReadOnlyList<string> Statuses = Enum.GetNames<ReportStatus>().Select(ToApiName).ToList();

    public static string ToApiName(string enumName) => char.ToLowerInvariant(enumName[0]) + enumName[1..];

    public static bool TryParseReason(string? value, out ReportReason reason) =>
        Enum.TryParse(value, ignoreCase: true, out reason) && Enum.IsDefined(reason);

    public static bool TryParseStatus(string? value, out ReportStatus status) =>
        Enum.TryParse(value, ignoreCase: true, out status) && Enum.IsDefined(status);
}

public sealed record ReportDto(
    Guid Id,
    Guid AdId,
    string AdTitle,
    // adState: "active" or "removed" while the ad exists, "deleted" when its owner deleted it.
    string AdState,
    Guid ReporterId,
    string ReporterDisplayName,
    string Reason,
    string? Details,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? ResolvedAtUtc);

/// <summary>A report with the ad it is about (null when the ad no longer exists).</summary>
public sealed record ReportView(AdReport Report, Ad? Ad);

public interface IReportRepository
{
    Task<bool> ExistsAsync(Guid adId, Guid reporterId, CancellationToken cancellationToken);

    /// <summary>The report with its reporter and its ad, tracked so it can be changed and saved.</summary>
    Task<ReportView?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The reports in that status (or all), the oldest first when they are open and the newest first otherwise. Page is 1-based.</summary>
    Task<(IReadOnlyList<ReportView> Items, int TotalCount)> GetPageAsync(ReportStatus? status, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>The open reports about an ad, tracked.</summary>
    Task<IReadOnlyList<AdReport>> GetOpenForAdAsync(Guid adId, CancellationToken cancellationToken);

    Task AddAsync(AdReport report, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal static class ReportMapper
{
    public static ReportDto ToDto(ReportView view)
    {
        var report = view.Report;

        var adState = view.Ad == null ? "deleted" : ReportRules.ToApiName(view.Ad.Status.ToString());

        return new ReportDto(
            report.Id,
            report.AdId,
            report.AdTitle,
            adState,
            report.ReporterId,
            report.Reporter?.DisplayName ?? string.Empty,
            ReportRules.ToApiName(report.Reason.ToString()),
            report.Details,
            ReportRules.ToApiName(report.Status.ToString()),
            report.CreatedAtUtc,
            report.ResolvedAtUtc);
    }
}

// ---- Report an ad ----

/// <summary>Reports a public ad of another user. A user can report an ad once.</summary>
public sealed record ReportAdCommand(Guid ReporterId, Guid AdId, string Reason, string? Details) : ICommand<Unit>;

public sealed class ReportAdCommandValidator : AbstractValidator<ReportAdCommand>
{
    public ReportAdCommandValidator(ILocalizer localizer)
    {
        RuleFor(x => x.Reason)
            .Must(reason => ReportRules.TryParseReason(reason, out _))
            .WithMessage(_ => localizer.Get(MessageKeys.ReportReasonInvalid, string.Join(", ", ReportRules.Reasons)));

        RuleFor(x => x.Details)
            .MaximumLength(ReportRules.MaxDetailsLength).WithMessage(_ => localizer.Get(MessageKeys.ReportDetailsTooLong, ReportRules.MaxDetailsLength));

        // "Other" says nothing by itself, so the reporter has to explain.
        RuleFor(x => x.Details)
            .NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.ReportDetailsRequired))
            .When(x => ReportRules.TryParseReason(x.Reason, out var reason) && reason == ReportReason.Other);
    }
}

public sealed class ReportAdCommandHandler : ICommandHandler<ReportAdCommand, Unit>
{
    private readonly IReportRepository _reports;
    private readonly IAdRepository _ads;
    private readonly TimeProvider _time;

    public ReportAdCommandHandler(IReportRepository reports, IAdRepository ads, TimeProvider time)
    {
        _reports = reports;
        _ads = ads;
        _time = time;
    }

    public async Task<Unit> HandleAsync(ReportAdCommand command, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var ad = await AdAccess.GetAsync(_ads, command.AdId, cancellationToken);

        if (!AdAccess.CanView(ad, Requester.Anonymous, now))
            throw new NotFoundException(MessageKeys.AdNotFound);

        if (ad.OwnerId == command.ReporterId)
            throw new ConflictException(MessageKeys.CannotReportOwnAd);

        if (await _reports.ExistsAsync(ad.Id, command.ReporterId, cancellationToken))
            throw new ConflictException(MessageKeys.AlreadyReported);

        ReportRules.TryParseReason(command.Reason, out var reason);

        await _reports.AddAsync(new AdReport
        {
            AdId = ad.Id,
            AdTitle = ad.Title,
            ReporterId = command.ReporterId,
            Reason = reason,
            Details = string.IsNullOrWhiteSpace(command.Details) ? null : command.Details.Trim(),
            CreatedAtUtc = now
        }, cancellationToken);
        await _reports.SaveChangesAsync(cancellationToken);

        return default;
    }
}

// ---- Admin: list ----

/// <summary>The reports for the admins. Status is open, resolved or dismissed; without it, all.</summary>
public sealed record GetReportsQuery(string? Status = null, int Page = 1, int PageSize = 20) : IQuery<PagedResult<ReportDto>>;

public sealed class GetReportsQueryValidator : AbstractValidator<GetReportsQuery>
{
    public GetReportsQueryValidator(ILocalizer localizer)
    {
        RuleFor(x => x.Status)
            .Must(status => ReportRules.TryParseStatus(status, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Status))
            .WithMessage(_ => localizer.Get(MessageKeys.ReportStatusInvalid, string.Join(", ", ReportRules.Statuses)));

        this.AddPagingRules(localizer, x => x.Page, x => x.PageSize);
    }
}

public sealed class GetReportsQueryHandler : IQueryHandler<GetReportsQuery, PagedResult<ReportDto>>
{
    private readonly IReportRepository _reports;

    public GetReportsQueryHandler(IReportRepository reports)
    {
        _reports = reports;
    }

    public async Task<PagedResult<ReportDto>> HandleAsync(GetReportsQuery query, CancellationToken cancellationToken)
    {
        ReportStatus? status = ReportRules.TryParseStatus(query.Status, out var parsed) ? parsed : null;

        var (items, totalCount) = await _reports.GetPageAsync(status, query.Page, query.PageSize, cancellationToken);

        return new PagedResult<ReportDto>(items.Select(ReportMapper.ToDto).ToList(), query.Page, query.PageSize, totalCount);
    }
}

// ---- Admin: decide ----

/// <summary>The ad is taken down and every open report about it is closed as resolved.</summary>
public sealed record ResolveReportCommand(Guid AdminId, Guid ReportId) : ICommand<ReportDto>;

public sealed class ResolveReportCommandHandler : ICommandHandler<ResolveReportCommand, ReportDto>
{
    private readonly IReportRepository _reports;
    private readonly TimeProvider _time;

    public ResolveReportCommandHandler(IReportRepository reports, TimeProvider time)
    {
        _reports = reports;
        _time = time;
    }

    public async Task<ReportDto> HandleAsync(ResolveReportCommand command, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var view = await _reports.GetByIdAsync(command.ReportId, cancellationToken) ?? throw new NotFoundException(MessageKeys.ReportNotFound);

        if (view.Report.Status != ReportStatus.Open)
            throw new ConflictException(MessageKeys.ReportAlreadyClosed);

        // The ad may be gone already (its owner deleted it); then there is nothing to take down and the report is just closed.
        if (view.Ad is { Status: not AdStatus.Removed } ad)
        {
            ad.Status = AdStatus.Removed;
            ad.UpdatedAtUtc = now;
        }

        foreach (var report in await _reports.GetOpenForAdAsync(view.Report.AdId, cancellationToken))
            Close(report, ReportStatus.Resolved, command.AdminId, now);

        // Whatever the repository returned, the report that was asked for is closed too.
        if (view.Report.Status == ReportStatus.Open)
            Close(view.Report, ReportStatus.Resolved, command.AdminId, now);

        await _reports.SaveChangesAsync(cancellationToken);

        return ReportMapper.ToDto(view);
    }

    private static void Close(AdReport report, ReportStatus status, Guid adminId, DateTime now)
    {
        report.Status = status;
        report.ResolvedAtUtc = now;
        report.ResolvedById = adminId;
    }
}

/// <summary>The admin looked at the report and the ad stays. Only this report is closed.</summary>
public sealed record DismissReportCommand(Guid AdminId, Guid ReportId) : ICommand<ReportDto>;

public sealed class DismissReportCommandHandler : ICommandHandler<DismissReportCommand, ReportDto>
{
    private readonly IReportRepository _reports;
    private readonly TimeProvider _time;

    public DismissReportCommandHandler(IReportRepository reports, TimeProvider time)
    {
        _reports = reports;
        _time = time;
    }

    public async Task<ReportDto> HandleAsync(DismissReportCommand command, CancellationToken cancellationToken)
    {
        var view = await _reports.GetByIdAsync(command.ReportId, cancellationToken) ?? throw new NotFoundException(MessageKeys.ReportNotFound);

        if (view.Report.Status != ReportStatus.Open)
            throw new ConflictException(MessageKeys.ReportAlreadyClosed);

        view.Report.Status = ReportStatus.Dismissed;
        view.Report.ResolvedAtUtc = _time.GetUtcNow().UtcDateTime;
        view.Report.ResolvedById = command.AdminId;

        await _reports.SaveChangesAsync(cancellationToken);

        return ReportMapper.ToDto(view);
    }
}
