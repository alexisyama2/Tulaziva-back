using Tulaziva.Application.Cqrs;

namespace Tulaziva.Application.Ads;

/// <summary>Gives the ad another full period of visibility, counted from now. Works also on an expired ad.</summary>
public sealed record RenewAdCommand(Guid UserId, Guid AdId) : ICommand<AdDto>;

public sealed class RenewAdCommandHandler : ICommandHandler<RenewAdCommand, AdDto>
{
    private readonly IAdRepository _ads;
    private readonly TimeProvider _time;

    public RenewAdCommandHandler(IAdRepository ads, TimeProvider time)
    {
        _ads = ads;
        _time = time;
    }

    public async Task<AdDto> HandleAsync(RenewAdCommand command, CancellationToken cancellationToken)
    {
        var ad = AdAccess.RequireOwner(await AdAccess.GetAsync(_ads, command.AdId, cancellationToken), command.UserId);
        AdAccess.RequireNotRemoved(ad);

        var now = _time.GetUtcNow().UtcDateTime;
        ad.ExpiresAtUtc = now.AddDays(AdRules.DurationDays);
        ad.UpdatedAtUtc = now;

        await _ads.SaveChangesAsync(cancellationToken);

        return AdMapper.ToDto(ad);
    }
}
