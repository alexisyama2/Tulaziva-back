using FluentValidation;
using Tulaziva.Application.Common;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;

namespace Tulaziva.Application.Users;

/// <summary>The data needed to create an account. Shared by the public registration and the creation of admins.</summary>
public interface INewUserData
{
    string Email { get; }
    string Password { get; }
    string DisplayName { get; }
    string? Language { get; }
    double? Latitude { get; }
    double? Longitude { get; }
}

/// <summary>The version of the terms of use and privacy notice that sign-ups accept. Change it when the texts change in a way users must accept again.</summary>
public static class LegalRules
{
    public const string TermsVersion = "2026-11-13";
}

public static class UserRules
{
    public const int MaxEmailLength = 256;
    public const int MaxDisplayNameLength = 100;
}

public abstract class NewUserDataValidator<T> : AbstractValidator<T> where T : INewUserData
{
    public const int MinPasswordLength = PasswordRules.MinLength;
    public const int MaxPasswordLength = PasswordRules.MaxLength;
    public const int MaxEmailLength = UserRules.MaxEmailLength;
    public const int MaxDisplayNameLength = UserRules.MaxDisplayNameLength;

    protected NewUserDataValidator(ILocalizer localizer)
    {
        RuleFor(x => x.Email).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.EmailRequired))
            .MaximumLength(MaxEmailLength).WithMessage(_ => localizer.Get(MessageKeys.EmailTooLong, MaxEmailLength))
            .EmailAddress().WithMessage(_ => localizer.Get(MessageKeys.EmailInvalid));

        this.AddPasswordRules(localizer, x => x.Password);

        RuleFor(x => x.DisplayName).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.DisplayNameRequired))
            .MaximumLength(MaxDisplayNameLength).WithMessage(_ => localizer.Get(MessageKeys.DisplayNameTooLong, MaxDisplayNameLength));

        RuleFor(x => x.Language)
            .Must(SupportedLanguages.IsSupported)
            .When(x => !string.IsNullOrWhiteSpace(x.Language))
            .WithMessage(_ => localizer.Get(MessageKeys.LanguageUnsupported, string.Join(", ", SupportedLanguages.All)));

        this.AddLocationRules(localizer, x => x.Latitude, x => x.Longitude);
    }
}

internal static class UserFactory
{
    public static User Create(INewUserData data, UserRole role, string passwordHash) => new()
    {
        Email = data.Email.Trim().ToLowerInvariant(),
        PasswordHash = passwordHash,
        DisplayName = data.DisplayName.Trim(),
        Role = role,
        PreferredLanguage = SupportedLanguages.Normalize(data.Language),
        RegistrationLatitude = data.Latitude,
        RegistrationLongitude = data.Longitude
    };
}
