using System.Linq.Expressions;
using FluentValidation;
using Tulaziva.Application.Localization;

namespace Tulaziva.Application.Common;

public static class ValidationRules
{
    /// <summary>
    /// Latitude and longitude, both optional, in range, and either both present or both missing:
    /// one coordinate alone is useless for searching by distance.
    /// </summary>
    public static void AddLocationRules<T>(
        this AbstractValidator<T> validator,
        ILocalizer localizer,
        Expression<Func<T, double?>> latitude,
        Expression<Func<T, double?>> longitude)
    {
        var getLatitude = latitude.Compile();
        var getLongitude = longitude.Compile();

        validator.RuleFor(latitude)
            .InclusiveBetween(-90.0, 90.0).WithMessage(_ => localizer.Get(MessageKeys.LatitudeOutOfRange))
            .When(x => getLatitude(x).HasValue);

        validator.RuleFor(longitude)
            .InclusiveBetween(-180.0, 180.0).WithMessage(_ => localizer.Get(MessageKeys.LongitudeOutOfRange))
            .When(x => getLongitude(x).HasValue);

        validator.RuleFor(x => x)
            .Must(x => getLatitude(x).HasValue == getLongitude(x).HasValue)
            .WithMessage(_ => localizer.Get(MessageKeys.LocationIncomplete))
            .OverridePropertyName("Location");
    }

    public static void AddPagingRules<T>(
        this AbstractValidator<T> validator,
        ILocalizer localizer,
        Expression<Func<T, int>> page,
        Expression<Func<T, int>> pageSize,
        int maxPageSize = 100)
    {
        validator.RuleFor(page).GreaterThanOrEqualTo(1).WithMessage(_ => localizer.Get(MessageKeys.PageInvalid));
        validator.RuleFor(pageSize).InclusiveBetween(1, maxPageSize).WithMessage(_ => localizer.Get(MessageKeys.PageSizeInvalid, maxPageSize));
    }
}
