using Tulaziva.Application.Ads;
using Tulaziva.Application.Auth;
using Tulaziva.Application.Blocks;
using Tulaziva.Application.Users;
using Tulaziva.Application.Chat;
using Tulaziva.Application.Favorites;
using Tulaziva.Application.Reports;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Tests.Fakes;

/// <summary>Records who was asked to verify the address or to reset the password.</summary>
public sealed class FakeAccountEmailService : IAccountEmailService
{
    public List<User> Verifications { get; } = new();

    public List<User> PasswordResets { get; } = new();

    public List<(User User, string NewEmail)> EmailChanges { get; } = new();

    public List<(User User, string OldEmail)> EmailChangedNotices { get; } = new();

    public Task SendEmailChangeAsync(User user, string newEmail, CancellationToken cancellationToken)
    {
        EmailChanges.Add((user, newEmail));
        return Task.CompletedTask;
    }

    public Task SendEmailChangedNoticeAsync(User user, string oldEmail, CancellationToken cancellationToken)
    {
        EmailChangedNotices.Add((user, oldEmail));
        return Task.CompletedTask;
    }

    public Task SendVerificationAsync(User user, CancellationToken cancellationToken)
    {
        Verifications.Add(user);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(User user, CancellationToken cancellationToken)
    {
        PasswordResets.Add(user);
        return Task.CompletedTask;
    }
}

public sealed class CapturingEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        lock (Sent)
            Sent.Add(message);

        return Task.CompletedTask;
    }

    /// <summary>The secret of the last link sent to this address (what comes after "token=").</summary>
    public string LastTokenFor(string email)
    {
        EmailMessage message;

        lock (Sent)
            message = Sent.Last(m => m.To == email);

        var start = message.Body.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
        var end = message.Body.IndexOfAny(new[] { '\n', ' ', '\r' }, start);

        return Uri.UnescapeDataString(message.Body[start..(end < 0 ? message.Body.Length : end)]);
    }
}

public sealed class StaticAppLinks : IAppLinks
{
    public string PasswordReset(string token) => $"https://app.test/reset-password?token={token}";

    public string VerifyEmail(string token) => $"https://app.test/verify-email?token={token}";

    public string ConfirmEmailChange(string token) => $"https://app.test/confirm-email-change?token={token}";
}

public sealed class FakeUserTokenRepository : IUserTokenRepository
{
    public List<UserToken> Tokens { get; } = new();

    public int SaveCount { get; private set; }

    public Task AddAsync(UserToken token, CancellationToken cancellationToken)
    {
        Tokens.Add(token);
        return Task.CompletedTask;
    }

    public Task<UserToken?> FindValidAsync(string tokenHash, TokenPurpose purpose, DateTime nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult(Tokens.FirstOrDefault(t => t.TokenHash == tokenHash && t.Purpose == purpose && t.UsedAtUtc == null && t.ExpiresAtUtc > nowUtc));

    public Task InvalidateAsync(Guid userId, TokenPurpose purpose, DateTime nowUtc, CancellationToken cancellationToken)
    {
        foreach (var token in Tokens.Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAtUtc == null))
            token.UsedAtUtc = nowUtc;

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

public sealed class FakeFavoriteRepository : IFavoriteRepository
{
    public List<Favorite> Favorites { get; } = new();

    public int SaveCount { get; private set; }

    public Task<bool> ExistsAsync(Guid userId, Guid adId, CancellationToken cancellationToken) =>
        Task.FromResult(Favorites.Any(f => f.UserId == userId && f.AdId == adId));

    public Task AddAsync(Favorite favorite, CancellationToken cancellationToken)
    {
        Favorites.Add(favorite);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid userId, Guid adId, CancellationToken cancellationToken)
    {
        Favorites.RemoveAll(f => f.UserId == userId && f.AdId == adId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> GetAdIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Favorites.Where(f => f.UserId == userId).OrderByDescending(f => f.CreatedAtUtc).Select(f => f.AdId).ToList());

    public Task<(IReadOnlyList<Ad> Items, int TotalCount)> GetPageAsync(Guid userId, int page, int pageSize, DateTime nowUtc, CancellationToken cancellationToken)
    {
        // The real repository reads the ad through the favorite; here the test sets Favorite.Ad.
        var visible = Favorites
            .Where(f => f.UserId == userId && f.Ad.Status == AdStatus.Active && f.Ad.ExpiresAtUtc > nowUtc)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => f.Ad)
            .ToList();

        IReadOnlyList<Ad> items = visible.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult((items, visible.Count));
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

public sealed class FakeReportRepository : IReportRepository
{
    private readonly FakeAdRepository _ads;
    private readonly FakeUserRepository _users;

    public FakeReportRepository(FakeAdRepository ads, FakeUserRepository users)
    {
        _ads = ads;
        _users = users;
    }

    public List<AdReport> Reports { get; } = new();

    public int SaveCount { get; private set; }

    public Task<bool> ExistsAsync(Guid adId, Guid reporterId, CancellationToken cancellationToken) =>
        Task.FromResult(Reports.Any(r => r.AdId == adId && r.ReporterId == reporterId));

    public Task<ReportView?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Reports.FirstOrDefault(r => r.Id == id) is { } report ? View(report) : null);

    public Task<(IReadOnlyList<ReportView> Items, int TotalCount)> GetPageAsync(ReportStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var filtered = Reports.Where(r => status == null || r.Status == status).ToList();
        var ordered = status == ReportStatus.Open ? filtered.OrderBy(r => r.CreatedAtUtc) : filtered.OrderByDescending(r => r.CreatedAtUtc);

        IReadOnlyList<ReportView> items = ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(View).ToList();

        return Task.FromResult((items, filtered.Count));
    }

    public Task<IReadOnlyList<AdReport>> GetOpenForAdAsync(Guid adId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AdReport>>(Reports.Where(r => r.AdId == adId && r.Status == ReportStatus.Open).ToList());

    public Task AddAsync(AdReport report, CancellationToken cancellationToken)
    {
        report.Reporter = _users.Users.FirstOrDefault(u => u.Id == report.ReporterId)!;
        Reports.Add(report);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    private ReportView View(AdReport report)
    {
        report.Reporter ??= _users.Users.FirstOrDefault(u => u.Id == report.ReporterId)!;
        return new ReportView(report, _ads.Ads.FirstOrDefault(a => a.Id == report.AdId));
    }
}

public sealed class RecordingNotifier : IRealtimeNotifier
{
    public List<(Guid RecipientId, MessageNotification Notification)> Messages { get; } = new();

    public List<(Guid UserId, Guid ConversationId)> Reads { get; } = new();

    public Task MessageReceivedAsync(Guid recipientId, MessageNotification notification, CancellationToken cancellationToken)
    {
        Messages.Add((recipientId, notification));
        return Task.CompletedTask;
    }

    public Task ConversationReadAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        Reads.Add((userId, conversationId));
        return Task.CompletedTask;
    }
}

public sealed class FakeBlockRepository : IBlockRepository
{
    public List<UserBlock> Blocks { get; } = new();

    public int SaveCount { get; private set; }

    public Task<bool> ExistsEitherWayAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Blocks.Any(b => (b.BlockerId == userId && b.BlockedId == otherUserId) || (b.BlockerId == otherUserId && b.BlockedId == userId)));

    public Task<bool> ExistsAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken) =>
        Task.FromResult(Blocks.Any(b => b.BlockerId == blockerId && b.BlockedId == blockedId));

    public Task AddAsync(UserBlock block, CancellationToken cancellationToken)
    {
        Blocks.Add(block);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken)
    {
        Blocks.RemoveAll(b => b.BlockerId == blockerId && b.BlockedId == blockedId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UserBlock>> GetByBlockerAsync(Guid blockerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserBlock>>(Blocks.Where(b => b.BlockerId == blockerId).OrderByDescending(b => b.CreatedAtUtc).ToList());

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

public sealed class FakeAccountDataRepository : IAccountDataRepository
{
    public List<Guid> Removed { get; } = new();

    public List<string> Files { get; } = new();

    public Task<IReadOnlyList<string>> RemoveOwnedDataAsync(Guid userId, CancellationToken cancellationToken)
    {
        Removed.Add(userId);
        return Task.FromResult<IReadOnlyList<string>>(Files.ToList());
    }
}

public sealed class FakeFeaturingPolicy : IFeaturingPolicy
{
    public bool OwnersCanFeature { get; set; } = true;
}

public static class TestSessions
{
    public static SessionService Create(FakeUserTokenRepository? tokens = null, TimeProvider? time = null) =>
        new(new FakeTokenService(), tokens ?? new FakeUserTokenRepository(), time ?? TimeProvider.System);
}

/// <summary>Remembers what the email log was told, in order.</summary>
public sealed class FakeEmailLogWriter : Tulaziva.Infrastructure.Email.IEmailLogWriter
{
    public List<(Guid Id, Tulaziva.Domain.Enums.EmailStatus Status, string To)> Recorded { get; } = new();

    public List<(Guid Id, int Attempts, string Error)> AttemptsFailed { get; } = new();

    public List<(Guid Id, int Attempts)> Sent { get; } = new();

    public List<(Guid Id, int Attempts, string Error)> Failed { get; } = new();

    public Task<Guid> RecordAsync(EmailMessage message, Tulaziva.Domain.Enums.EmailStatus status)
    {
        var id = Guid.NewGuid();
        Recorded.Add((id, status, message.To));
        return Task.FromResult(id);
    }

    public Task RecordAttemptFailedAsync(Guid id, int attempts, string error)
    {
        AttemptsFailed.Add((id, attempts, error));
        return Task.CompletedTask;
    }

    public Task RecordSentAsync(Guid id, int attempts)
    {
        Sent.Add((id, attempts));
        return Task.CompletedTask;
    }

    public Task RecordFailedAsync(Guid id, int attempts, string error)
    {
        Failed.Add((id, attempts, error));
        return Task.CompletedTask;
    }
}

public sealed class FakeEmailLogRepository : Tulaziva.Application.Admin.IEmailLogRepository
{
    public List<EmailLog> Logs { get; } = new();

    public Task<(IReadOnlyList<EmailLog> Items, int TotalCount)> GetPageAsync(EmailStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var filtered = Logs.Where(l => status == null || l.Status == status).OrderByDescending(l => l.CreatedAtUtc).ToList();
        IReadOnlyList<EmailLog> items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult((items, filtered.Count));
    }
}

public sealed class FakeEmailDiagnostics : Tulaziva.Application.Admin.IEmailDiagnostics
{
    public string? LastTo { get; private set; }

    public Task<Tulaziva.Application.Admin.EmailTestResult> SendTestAsync(string to, CancellationToken cancellationToken)
    {
        LastTo = to;
        return Task.FromResult(new Tulaziva.Application.Admin.EmailTestResult(true, true, Array.Empty<string>(), "from@mail.com", null));
    }
}
