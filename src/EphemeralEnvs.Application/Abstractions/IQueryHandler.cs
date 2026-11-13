namespace EphemeralEnvs.Application.Abstractions;

/// <summary>Reads data (read side). Never changes state.</summary>
public interface IQueryHandler<in TQuery, TResult>
{
    public Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
