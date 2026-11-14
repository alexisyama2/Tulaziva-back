using FluentValidation;
using Tulaziva.Application.Common;
using Tulaziva.Application.Localization;

namespace Tulaziva.Application.Ads;

/// <summary>The data an owner fills in to create or edit an ad.</summary>
public interface IAdData
{
    int CategoryId { get; }
    string Title { get; }
    string Description { get; }
    decimal Price { get; }
    string Currency { get; }
    double? Latitude { get; }
    double? Longitude { get; }
}

public abstract class AdDataValidator<T> : AbstractValidator<T> where T : IAdData
{
    protected AdDataValidator(ILocalizer localizer, ICategoryRepository categories)
    {
        RuleFor(x => x.CategoryId)
            .MustAsync((id, cancellationToken) => categories.ExistsAsync(id, cancellationToken))
            .WithMessage(_ => localizer.Get(MessageKeys.CategoryInvalid));

        RuleFor(x => x.Title).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.TitleRequired))
            .Must(title => title.Trim().Length is >= AdRules.MinTitleLength and <= AdRules.MaxTitleLength)
            .WithMessage(_ => localizer.Get(MessageKeys.TitleLength, AdRules.MinTitleLength, AdRules.MaxTitleLength));

        RuleFor(x => x.Description).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(_ => localizer.Get(MessageKeys.DescriptionRequired))
            .MaximumLength(AdRules.MaxDescriptionLength).WithMessage(_ => localizer.Get(MessageKeys.DescriptionTooLong, AdRules.MaxDescriptionLength));

        RuleFor(x => x.Price)
            .InclusiveBetween(0m, AdRules.MaxPrice).WithMessage(_ => localizer.Get(MessageKeys.PriceInvalid, AdRules.MaxPrice));

        RuleFor(x => x.Currency)
            .Must(currency => currency != null && AdRules.Currencies.Contains(currency.Trim().ToUpperInvariant()))
            .WithMessage(_ => localizer.Get(MessageKeys.CurrencyUnsupported, string.Join(", ", AdRules.Currencies)));

        this.AddLocationRules(localizer, x => x.Latitude, x => x.Longitude);
    }
}
