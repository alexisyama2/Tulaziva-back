using FluentAssertions;
using Tulaziva.Application.Common;
using Tulaziva.Application.Localization;
using Tulaziva.Application.Reports;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Reports;

public class ReportsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeUserRepository _users = new();
    private readonly FakeAdRepository _ads = new();
    private readonly FakeReportRepository _reports;
    private readonly MutableTimeProvider _time = new(Now);
    private readonly User _owner = new() { Email = "owner@mail.com", DisplayName = "Owner" };
    private readonly User _luis = new() { Email = "luis@mail.com", DisplayName = "Luis" };
    private readonly User _marta = new() { Email = "marta@mail.com", DisplayName = "Marta" };
    private readonly User _admin = new() { Email = "admin@mail.com", DisplayName = "Admin", Role = UserRole.Admin };
    private readonly Ad _ad;

    public ReportsTests()
    {
        _users.Users.AddRange(new[] { _owner, _luis, _marta, _admin });
        _reports = new FakeReportRepository(_ads, _users);
        _ad = new Ad { OwnerId = _owner.Id, Owner = _owner, CategoryId = 1, Title = "Sospechoso", ExpiresAtUtc = Now.UtcDateTime.AddDays(10) };
        _ads.Ads.Add(_ad);
    }

    private Task Report(User reporter, string reason = "spam", string? details = null, Guid? adId = null) =>
        new ReportAdCommandHandler(_reports, _ads, _time).HandleAsync(new ReportAdCommand(reporter.Id, adId ?? _ad.Id, reason, details), CancellationToken.None);

    // ---- Report ----

    [Fact]
    public async Task Report_StoresTheComplaintWithATitleSnapshot()
    {
        await Report(_luis, "fraud", "  Asks for money first ");

        var report = _reports.Reports.Should().ContainSingle().Subject;
        report.AdId.Should().Be(_ad.Id);
        report.AdTitle.Should().Be("Sospechoso");
        report.ReporterId.Should().Be(_luis.Id);
        report.Reason.Should().Be(ReportReason.Fraud);
        report.Details.Should().Be("Asks for money first");
        report.Status.Should().Be(ReportStatus.Open);
        report.CreatedAtUtc.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task Report_TheReasonIsCaseInsensitive()
    {
        await Report(_luis, "INAPPROPRIATE");

        _reports.Reports.Single().Reason.Should().Be(ReportReason.Inappropriate);
    }

    [Fact]
    public async Task Report_YourOwnAd_ThrowsConflict()
    {
        var act = () => Report(_owner);

        await act.Should().ThrowAsync<ConflictException>().Where(e => e.MessageKey == MessageKeys.CannotReportOwnAd);
    }

    [Fact]
    public async Task Report_TheSameAdTwice_ThrowsConflict_ButAnotherUserCanReportIt()
    {
        await Report(_luis);

        var again = () => Report(_luis, "fraud");
        await again.Should().ThrowAsync<ConflictException>().Where(e => e.MessageKey == MessageKeys.AlreadyReported);

        await Report(_marta);
        _reports.Reports.Should().HaveCount(2);
    }

    [Fact]
    public async Task Report_AnAdThatIsNotPublic_ThrowsNotFound()
    {
        _ad.Status = AdStatus.Removed;

        var act = () => Report(_luis);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Report_AnUnknownAd_ThrowsNotFound()
    {
        var act = () => Report(_luis, adId: Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData("spam", null, true)]
    [InlineData("fraud", "", true)]
    [InlineData("other", null, false)]
    [InlineData("other", "   ", false)]
    [InlineData("other", "Something odd", true)]
    [InlineData("nonsense", null, false)]
    [InlineData("", null, false)]
    [InlineData("42", null, false)]
    public void Validator_ChecksTheReasonAndRequiresDetailsForOther(string reason, string? details, bool valid)
    {
        new ReportAdCommandValidator(new Localizer()).Validate(new ReportAdCommand(Guid.NewGuid(), Guid.NewGuid(), reason, details)).IsValid.Should().Be(valid);
    }

    [Fact]
    public void Validator_RefusesLongDetails()
    {
        var command = new ReportAdCommand(Guid.NewGuid(), Guid.NewGuid(), "spam", new string('x', ReportRules.MaxDetailsLength + 1));

        new ReportAdCommandValidator(new Localizer()).Validate(command).IsValid.Should().BeFalse();
    }

    // ---- Admin: list ----

    [Fact]
    public async Task List_OpenReportsComeOldestFirst_AndTheDtoSaysWhatHappenedToTheAd()
    {
        await Report(_luis);
        _time.Advance(TimeSpan.FromHours(1));
        await Report(_marta, "fraud");
        var deletedAd = new Ad { OwnerId = _owner.Id, CategoryId = 1, Title = "Gone", ExpiresAtUtc = Now.UtcDateTime.AddDays(5) };
        _reports.Reports.Add(new AdReport { AdId = deletedAd.Id, AdTitle = "Gone", ReporterId = _luis.Id, Reporter = _luis, CreatedAtUtc = Now.UtcDateTime.AddHours(2) });

        var page = await new GetReportsQueryHandler(_reports).HandleAsync(new GetReportsQuery("open"), CancellationToken.None);

        page.TotalCount.Should().Be(3);
        page.Items.Select(r => r.ReporterDisplayName).Should().Equal("Luis", "Marta", "Luis");
        page.Items[0].AdState.Should().Be("active");
        page.Items[0].Reason.Should().Be("spam");
        page.Items[1].Reason.Should().Be("fraud");
        page.Items[2].AdState.Should().Be("deleted");
    }

    [Fact]
    public async Task List_FiltersByStatus_AndWithoutStatusReturnsAll()
    {
        await Report(_luis);
        await Report(_marta);
        _reports.Reports[0].Status = ReportStatus.Dismissed;
        var handler = new GetReportsQueryHandler(_reports);

        (await handler.HandleAsync(new GetReportsQuery("open"), CancellationToken.None)).TotalCount.Should().Be(1);
        (await handler.HandleAsync(new GetReportsQuery("dismissed"), CancellationToken.None)).TotalCount.Should().Be(1);
        (await handler.HandleAsync(new GetReportsQuery(), CancellationToken.None)).TotalCount.Should().Be(2);
    }

    [Theory]
    [InlineData("open", true)]
    [InlineData("Resolved", true)]
    [InlineData(null, true)]
    [InlineData("pending", false)]
    public void ListValidator_ChecksTheStatus(string? status, bool valid)
    {
        new GetReportsQueryValidator(new Localizer()).Validate(new GetReportsQuery(status)).IsValid.Should().Be(valid);
    }

    // ---- Admin: decide ----

    [Fact]
    public async Task Resolve_TakesTheAdDown_AndClosesEveryOpenReportAboutIt()
    {
        await Report(_luis);
        await Report(_marta, "fraud");
        var first = _reports.Reports[0];

        var dto = await new ResolveReportCommandHandler(_reports, _time).HandleAsync(new ResolveReportCommand(_admin.Id, first.Id), CancellationToken.None);

        _ad.Status.Should().Be(AdStatus.Removed);
        _reports.Reports.Should().OnlyContain(r => r.Status == ReportStatus.Resolved && r.ResolvedById == _admin.Id && r.ResolvedAtUtc == Now.UtcDateTime);
        dto.Status.Should().Be("resolved");
        dto.AdState.Should().Be("removed");
    }

    [Fact]
    public async Task Resolve_WhenTheOwnerDeletedTheAd_JustClosesTheReport()
    {
        await Report(_luis);
        _ads.Ads.Clear();

        var dto = await new ResolveReportCommandHandler(_reports, _time).HandleAsync(new ResolveReportCommand(_admin.Id, _reports.Reports[0].Id), CancellationToken.None);

        dto.Status.Should().Be("resolved");
        dto.AdState.Should().Be("deleted");
    }

    [Fact]
    public async Task Dismiss_ClosesOnlyThatReport_AndLeavesTheAd()
    {
        await Report(_luis);
        await Report(_marta);

        var dto = await new DismissReportCommandHandler(_reports, _time).HandleAsync(new DismissReportCommand(_admin.Id, _reports.Reports[0].Id), CancellationToken.None);

        dto.Status.Should().Be("dismissed");
        _ad.Status.Should().Be(AdStatus.Active);
        _reports.Reports[1].Status.Should().Be(ReportStatus.Open);
    }

    [Fact]
    public async Task Decide_AReportThatWasAlreadyClosed_ThrowsConflict()
    {
        await Report(_luis);
        var id = _reports.Reports[0].Id;
        await new DismissReportCommandHandler(_reports, _time).HandleAsync(new DismissReportCommand(_admin.Id, id), CancellationToken.None);

        var resolve = () => new ResolveReportCommandHandler(_reports, _time).HandleAsync(new ResolveReportCommand(_admin.Id, id), CancellationToken.None);
        var dismiss = () => new DismissReportCommandHandler(_reports, _time).HandleAsync(new DismissReportCommand(_admin.Id, id), CancellationToken.None);

        await resolve.Should().ThrowAsync<ConflictException>();
        await dismiss.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Decide_AnUnknownReport_ThrowsNotFound()
    {
        var act = () => new ResolveReportCommandHandler(_reports, _time).HandleAsync(new ResolveReportCommand(_admin.Id, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
