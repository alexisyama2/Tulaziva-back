namespace Tulaziva.Application.Localization;

public interface ILocalizer
{
    /// <summary>The message for the key in the language of the current request. Returns the key itself when it has no translation.</summary>
    string Get(string key, params object[] args);
}
