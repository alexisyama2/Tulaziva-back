using Tulaziva.Application.Abstractions;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Entities;

namespace Tulaziva.Application.Ads;

/// <summary>
/// Publishes an ad. Without latitude and longitude it takes the location where its owner registered.
/// UserId is the signed-in user; the API fills it from the token, never from the body.
/// </summary>
public sealed record CreateAdCommand(
    Guid UserId,
    int CategoryId,
    string Title,
    string Description,
    decimal Price,
    string Currency,
    double? Latitude = null,
    double? Longitude = null) : ICommand<AdDto>, IAdData;

public sealed class CreateAdCommandValidator : AdDataValidator<CreateAdCommand>
{
    public CreateAdCommandValidator(ILocalizer localizer, ICategoryRepository categories) : base(localizer, categories) { }
}

public sealed class CreateAdCommandHandler : ICommandHandler<CreateAdCommand, AdDto>
{
    private readonly IAdRepository _ads;
    private readonly IUserRepository _users;
    private readonly TimeProvider _time;

    public CreateAdCommandHandler(IAdRepository ads, IUserRepository users, TimeProvider time)
    {
        _ads = ads;
        _users = users;
        _time = time;
    }

    public async Task<AdDto> HandleAsync(CreateAdCommand command, CancellationToken cancellationToken)
    {
        var owner = await _users.GetByIdAsync(command.UserId, cancellationToken);

        if (owner == null || !owner.IsActive)
            throw new NotFoundException(MessageKeys.UserNotFound);

        var now = _time.GetUtcNow().UtcDateTime;
        var hasLocation = command.Latitude.HasValue && command.Longitude.HasValue;

        var ad = new Ad
        {
            OwnerId = owner.Id,
            Owner = owner,
            CategoryId = command.CategoryId,
            Title = command.Title.Trim(),
            Description = command.Description.Trim(),
            Price = command.Price,
            Currency = command.Currency.Trim().ToUpperInvariant(),
            Latitude = hasLocation ? command.Latitude : owner.RegistrationLatitude,
            Longitude = hasLocation ? command.Longitude : owner.RegistrationLongitude,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(AdRules.DurationDays)
        };

        await _ads.AddAsync(ad, cancellationToken);
        await _ads.SaveChangesAsync(cancellationToken);

        return AdMapper.ToDto(ad);
    }
}
