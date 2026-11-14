using Tulaziva.Application.Cqrs;

namespace Tulaziva.Application.Ads;

/// <summary>Deletes an ad of the signed-in user, with its images.</summary>
public sealed record DeleteAdCommand(Guid UserId, Guid AdId) : ICommand<Unit>;

public sealed class DeleteAdCommandHandler : ICommandHandler<DeleteAdCommand, Unit>
{
    private readonly IAdRepository _ads;
    private readonly IFileStorage _storage;

    public DeleteAdCommandHandler(IAdRepository ads, IFileStorage storage)
    {
        _ads = ads;
        _storage = storage;
    }

    public async Task<Unit> HandleAsync(DeleteAdCommand command, CancellationToken cancellationToken)
    {
        var ad = AdAccess.RequireOwner(await AdAccess.GetAsync(_ads, command.AdId, cancellationToken), command.UserId);
        var paths = ad.Images.Select(i => i.StoragePath).ToList();

        _ads.Remove(ad);
        await _ads.SaveChangesAsync(cancellationToken);

        // After the database: if a file cannot be removed it is only an orphan file, while the other order could leave an ad with broken images.
        foreach (var path in paths)
        {
            try
            {
                await _storage.DeleteAsync(path, cancellationToken);
            }
            catch (IOException)
            {
            }
        }

        return default;
    }
}
