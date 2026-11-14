using Tulaziva.Application.Abstractions;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;

namespace Tulaziva.Application.Ads;

/// <summary>Edits an ad of the signed-in user. Without latitude and longitude the location of the ad stays as it was.</summary>
public sealed record UpdateAdCommand(
    Guid UserId,
    Guid AdId,
    int CategoryId,
    string Title,
    string Description,
    decimal Price,
    string Currency,
    double? Latitude = null,
    double? Longitude = null) : ICommand<AdDto>, IAdData;

public sealed class UpdateAdCommandValidator : AdDataValidator<UpdateAdCommand>
{
    public UpdateAdCommandValidator(ILocalizer localizer, ICategoryRepository categories) : base(localizer, categories) { }
}

public sealed class UpdateAdCommandHandler : ICommandHandler<UpdateAdCommand, AdDto>
{
    private readonly IAdRepository _ads;
    private readonly TimeProvider _time;

    public UpdateAdCommandHandler(IAdRepository ads, TimeProvider time)
    {
        _ads = ads;
        _time = time;
    }

    public async Task<AdDto> HandleAsync(UpdateAdCommand command, CancellationToken cancellationToken)
    {
        var ad = AdAccess.RequireOwner(await AdAccess.GetAsync(_ads, command.AdId, cancellationToken), command.UserId);
        AdAccess.RequireNotRemoved(ad);

        ad.CategoryId = command.CategoryId;
        ad.Title = command.Title.Trim();
        ad.Description = command.Description.Trim();
        ad.Price = command.Price;
        ad.Currency = command.Currency.Trim().ToUpperInvariant();

        if (command.Latitude.HasValue && command.Longitude.HasValue)
        {
            ad.Latitude = command.Latitude;
            ad.Longitude = command.Longitude;
        }

        ad.UpdatedAtUtc = _time.GetUtcNow().UtcDateTime;

        await _ads.SaveChangesAsync(cancellationToken);

        return AdMapper.ToDto(ad);
    }
}
