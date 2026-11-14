namespace Tulaziva.Application.Cqrs;

/// <summary>The result of a command that has nothing to return.</summary>
public readonly record struct Unit;

/// <summary>A request that changes state.</summary>
public interface ICommand<TResult> { }

/// <summary>A request that only reads.</summary>
public interface IQuery<TResult> { }

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

/// <summary>Single entry point of the endpoints: validates the request, finds its handler and runs it.</summary>
public interface IDispatcher
{
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
}
