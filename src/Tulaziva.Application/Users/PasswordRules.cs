using System.Linq.Expressions;
using FluentValidation;
using Tulaziva.Application.Localization;

namespace Tulaziva.Application.Users;

/// <summary>The rules of a new password, shared by the sign-up, the password change and the password reset.</summary>
public static class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static void AddPasswordRules<T>(this AbstractValidator<T> validator, ILocalizer localizer, Expression<Func<T, string>> password)
    {
        validator.RuleFor(password).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.PasswordRequired))
            .MinimumLength(MinLength).WithMessage(_ => localizer.Get(MessageKeys.PasswordTooShort, MinLength))
            .MaximumLength(MaxLength).WithMessage(_ => localizer.Get(MessageKeys.PasswordTooLong, MaxLength))
            .Must(value => value.Any(char.IsLetter) && value.Any(char.IsDigit))
            .WithMessage(_ => localizer.Get(MessageKeys.PasswordNeedsLetterAndDigit));
    }
}
