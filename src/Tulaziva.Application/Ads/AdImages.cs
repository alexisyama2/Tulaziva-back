using FluentValidation;
using FluentValidation.Results;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Entities;

namespace Tulaziva.Application.Ads;

// ---- Upload ----

/// <summary>
/// Adds an image to an ad of the signed-in user. Length is what the server measured, not what the client declared.
/// The type is decided by the first bytes of the content, and the name of the stored file is generated.
/// </summary>
public sealed record AddAdImageCommand(Guid UserId, Guid AdId, string FileName, long Length, Stream Content) : ICommand<AdImageDto>;

public sealed class AddAdImageCommandValidator : AbstractValidator<AddAdImageCommand>
{
    public AddAdImageCommandValidator(ILocalizer localizer)
    {
        RuleFor(x => x.FileName).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.FileRequired))
            .Must(name => AdRules.ImageExtensions.Contains(Path.GetExtension(name).ToLowerInvariant()))
            .WithMessage(_ => localizer.Get(MessageKeys.FileExtensionUnsupported, string.Join(", ", AdRules.ImageExtensions)));

        RuleFor(x => x.Length).Cascade(CascadeMode.Stop)
            .GreaterThan(0).WithMessage(_ => localizer.Get(MessageKeys.FileRequired))
            .LessThanOrEqualTo(AdRules.MaxImageBytes).WithMessage(_ => localizer.Get(MessageKeys.FileTooLarge, AdRules.MaxImageBytes / (1024 * 1024)));
    }
}

public sealed class AddAdImageCommandHandler : ICommandHandler<AddAdImageCommand, AdImageDto>
{
    private readonly IAdRepository _ads;
    private readonly IFileStorage _storage;
    private readonly ILocalizer _localizer;
    private readonly TimeProvider _time;

    public AddAdImageCommandHandler(IAdRepository ads, IFileStorage storage, ILocalizer localizer, TimeProvider time)
    {
        _ads = ads;
        _storage = storage;
        _localizer = localizer;
        _time = time;
    }

    public async Task<AdImageDto> HandleAsync(AddAdImageCommand command, CancellationToken cancellationToken)
    {
        var ad = AdAccess.RequireOwner(await AdAccess.GetAsync(_ads, command.AdId, cancellationToken), command.UserId);
        AdAccess.RequireNotRemoved(ad);

        if (ad.Images.Count >= AdRules.MaxImagesPerAd)
            throw new ConflictException(MessageKeys.TooManyImages);

        using var upload = await UploadStreams.PeekAsync(command.Content, ImageSignature.HeaderLength, cancellationToken);

        var detected = ImageSignature.Detect(upload.Header)
            ?? throw new ValidationException(new[] { new ValidationFailure("File", _localizer.Get(MessageKeys.FileContentInvalid)) });

        var path = await _storage.SaveAsync($"ads/{ad.Id:N}", upload.Content, detected.Extension, cancellationToken);

        var image = new AdImage
        {
            AdId = ad.Id,
            StoragePath = path,
            ContentType = detected.ContentType,
            SizeBytes = command.Length,
            Position = ad.Images.Count == 0 ? 0 : ad.Images.Max(i => i.Position) + 1,
            CreatedAtUtc = _time.GetUtcNow().UtcDateTime
        };

        _ads.AddImage(image);

        try
        {
            await _ads.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _storage.DeleteAsync(path, CancellationToken.None);
            throw;
        }

        return AdMapper.ToDto(image);
    }
}

// ---- Delete ----

public sealed record DeleteAdImageCommand(Guid UserId, Guid AdId, Guid ImageId) : ICommand<Unit>;

public sealed class DeleteAdImageCommandHandler : ICommandHandler<DeleteAdImageCommand, Unit>
{
    private readonly IAdRepository _ads;
    private readonly IFileStorage _storage;

    public DeleteAdImageCommandHandler(IAdRepository ads, IFileStorage storage)
    {
        _ads = ads;
        _storage = storage;
    }

    public async Task<Unit> HandleAsync(DeleteAdImageCommand command, CancellationToken cancellationToken)
    {
        var ad = AdAccess.RequireOwner(await AdAccess.GetAsync(_ads, command.AdId, cancellationToken), command.UserId);

        var image = ad.Images.FirstOrDefault(i => i.Id == command.ImageId)
            ?? throw new NotFoundException(MessageKeys.ImageNotFound);

        _ads.RemoveImage(image);
        await _ads.SaveChangesAsync(cancellationToken);

        try
        {
            await _storage.DeleteAsync(image.StoragePath, cancellationToken);
        }
        catch (IOException)
        {
            // Only an orphan file is left; the image is already gone from the ad.
        }

        return default;
    }
}

// ---- Download ----

/// <summary>The image of an ad. Follows the same visibility as the ad: public while the ad is, always for its owner and the admins.</summary>
public sealed record GetAdImageQuery(Guid AdId, Guid ImageId, Requester Requester) : IQuery<AdImageFile>;

public sealed class GetAdImageQueryHandler : IQueryHandler<GetAdImageQuery, AdImageFile>
{
    private readonly IAdRepository _ads;
    private readonly IFileStorage _storage;
    private readonly TimeProvider _time;

    public GetAdImageQueryHandler(IAdRepository ads, IFileStorage storage, TimeProvider time)
    {
        _ads = ads;
        _storage = storage;
        _time = time;
    }

    public async Task<AdImageFile> HandleAsync(GetAdImageQuery query, CancellationToken cancellationToken)
    {
        var ad = await AdAccess.GetAsync(_ads, query.AdId, cancellationToken);

        if (!AdAccess.CanView(ad, query.Requester, _time.GetUtcNow().UtcDateTime))
            throw new NotFoundException(MessageKeys.AdNotFound);

        var image = ad.Images.FirstOrDefault(i => i.Id == query.ImageId)
            ?? throw new NotFoundException(MessageKeys.ImageNotFound);

        var stream = await _storage.OpenReadAsync(image.StoragePath, cancellationToken)
            ?? throw new NotFoundException(MessageKeys.ImageNotFound);

        return new AdImageFile(stream, image.ContentType);
    }
}
