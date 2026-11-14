namespace Tulaziva.Application.Ads;

/// <summary>The limits of the ads.</summary>
public static class AdRules
{
    /// <summary>How long an ad stays visible after it is published or renewed.</summary>
    public const int DurationDays = 30;

    public const int MaxImagesPerAd = 10;
    public const long MaxImageBytes = 5 * 1024 * 1024;

    public const int MinTitleLength = 3;
    public const int MaxTitleLength = 120;
    public const int MaxDescriptionLength = 4000;
    public const decimal MaxPrice = 999_999_999.99m;

    public const int MaxSearchTextLength = 100;
    public const double MaxSearchRadiusKm = 500;

    public static readonly IReadOnlyList<string> Currencies = new[] { "MXN", "USD", "EUR", "COP", "ARS", "CLP", "PEN", "BRL" };

    /// <summary>The extensions the client may send. What is stored is decided by the content of the file, not by its name.</summary>
    public static readonly IReadOnlyList<string> ImageExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
}
