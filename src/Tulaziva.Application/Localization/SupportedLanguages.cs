namespace Tulaziva.Application.Localization;

public static class SupportedLanguages
{
    public const string Default = "es";

    public static readonly IReadOnlyList<string> All = new[] { "es", "en" };

    public static bool IsSupported(string? code) =>
        code != null && All.Contains(code, StringComparer.OrdinalIgnoreCase);

    /// <summary>The supported code for the given one, or the default when it is empty or not supported.</summary>
    public static string Normalize(string? code) =>
        IsSupported(code) ? code!.ToLowerInvariant() : Default;
}
