using System.Globalization;

namespace Tulaziva.Application.Localization;

/// <summary>Reads the language from CurrentUICulture, which the API sets for every request from the Accept-Language header.</summary>
public sealed class Localizer : ILocalizer
{
    public string Get(string key, params object[] args)
    {
        var language = SupportedLanguages.Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

        if (!Messages.Translations.TryGetValue(language, out var messages) || !messages.TryGetValue(key, out var template))
            return key;

        return args.Length == 0 ? template : string.Format(CultureInfo.CurrentCulture, template, args);
    }
}
