using Microsoft.EntityFrameworkCore;
using Tulaziva.Application.Reports;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Infrastructure.Persistence;

public sealed class ReportRepository : IReportRepository
{
    private readonly TulazivaDbContext _db;

    public ReportRepository(TulazivaDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsAsync(Guid adId, Guid reporterId, CancellationToken cancellationToken) =>
        _db.AdReports.AnyAsync(r => r.AdId == adId && r.ReporterId == reporterId, cancellationToken);

    public async Task<ReportView?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var report = await _db.AdReports.Include(r => r.Reporter).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (report == null)
            return null;

        // The ad may be gone: the report keeps its id without a foreign key.
        var ad = await _db.Ads.FirstOrDefaultAsync(a => a.Id == report.AdId, cancellationToken);

        return new ReportView(report, ad);
    }

    public async Task<(IReadOnlyList<ReportView> Items, int TotalCount)> GetPageAsync(ReportStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _db.AdReports.AsNoTracking().Where(r => status == null || r.Status == status);

        var totalCount = await query.CountAsync(cancellationToken);

        // What waits for an admin is read in the order it arrived; what is closed, the latest first.
        var ordered = status == ReportStatus.Open
            ? query.OrderBy(r => r.CreatedAtUtc)
            : query.OrderByDescending(r => r.CreatedAtUtc);

        var reports = await ordered
            .ThenBy(r => r.Id)
            .Include(r => r.Reporter)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var adIds = reports.Select(r => r.AdId).Distinct().ToList();
        var ads = await _db.Ads.AsNoTracking().Where(a => adIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);

        return (reports.Select(r => new ReportView(r, ads.GetValueOrDefault(r.AdId))).ToList(), totalCount);
    }

    public async Task<IReadOnlyList<AdReport>> GetOpenForAdAsync(Guid adId, CancellationToken cancellationToken) =>
        await _db.AdReports.Where(r => r.AdId == adId && r.Status == ReportStatus.Open).ToListAsync(cancellationToken);

    public async Task AddAsync(AdReport report, CancellationToken cancellationToken) =>
        await _db.AdReports.AddAsync(report, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _db.SaveChangesAsync(cancellationToken);
}
