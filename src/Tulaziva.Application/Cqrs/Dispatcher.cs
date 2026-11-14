using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Tulaziva.Application.Cqrs;

public sealed class Dispatcher : IDispatcher
{
    private readonly IServiceProvider _services;

    public Dispatcher(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(command, cancellationToken);

        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResult));
        dynamic handler = _services.GetRequiredService(handlerType);

        return await (Task<TResult>)handler.HandleAsync((dynamic)command, cancellationToken);
    }

    public async Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(query, cancellationToken);

        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResult));
        dynamic handler = _services.GetRequiredService(handlerType);

        return await (Task<TResult>)handler.HandleAsync((dynamic)query, cancellationToken);
    }

    // Every validator registered for the request type runs; all the failures are reported together.
    private async Task ValidateAsync(object request, CancellationToken cancellationToken)
    {
        var validatorType = typeof(IValidator<>).MakeGenericType(request.GetType());
        var failures = new List<ValidationFailure>();

        foreach (var validator in _services.GetServices(validatorType).OfType<IValidator>())
        {
            var result = await validator.ValidateAsync(new ValidationContext<object>(request), cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
            throw new ValidationException(failures);
    }
}
