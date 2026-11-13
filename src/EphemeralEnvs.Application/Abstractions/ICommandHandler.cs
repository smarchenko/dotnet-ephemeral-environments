namespace EphemeralEnvs.Application.Abstractions;

/// <summary>Changes state (write side). Returns as little as possible, e.g. the id of a created entity.</summary>
public interface ICommandHandler<in TCommand, TResult>
{
    public Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
