using Tulaziva.Application.Cqrs;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Application.Ads;

/// <summary>Takes an ad down. Only an admin can send it; the API enforces that. The ad is kept, so the owner can still see it.</summary>
public sealed record RemoveAdCommand(Guid AdId) : ICommand<Unit>;

public sealed class RemoveAdCommandHandler : ICommandHandler<RemoveAdCommand, Unit>
{
    private readonly IAdRepository _ads;
    private readonly TimeProvider _time;

    public RemoveAdCommandHandler(IAdRepository ads, TimeProvider time)
    {
        _ads = ads;
        _time = time;
    }

    public async Task<Unit> HandleAsync(RemoveAdCommand command, CancellationToken cancellationToken)
    {
        var ad = await AdAccess.GetAsync(_ads, command.AdId, cancellationToken);

        if (ad.Status != AdStatus.Removed)
        {
            ad.Status = AdStatus.Removed;
            ad.UpdatedAtUtc = _time.GetUtcNow().UtcDateTime;
            await _ads.SaveChangesAsync(cancellationToken);
        }

        return default;
    }
}
