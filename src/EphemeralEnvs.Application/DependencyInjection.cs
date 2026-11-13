using EphemeralEnvs.Application.Abstractions;
using EphemeralEnvs.Application.Persistence;
using EphemeralEnvs.Application.Products;
using EphemeralEnvs.Contracts;
using EphemeralEnvs.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql;

namespace EphemeralEnvs.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the command/query handlers and both DbContexts (write: <c>AppDbContext</c>,
    /// read: <c>ReadDbContext</c> behind <c>IReadDb</c>, on a read-only session). The connection string <c>ConnectionStrings:Default</c> is read when a
    /// context is created (not at startup), so test overrides apply.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddDbContext<AppDbContext>((sp, options) => options.UseNpgsql(GetConnectionString(sp)));
        services.AddDbContext<ReadDbContext>((sp, options) => options.UseNpgsql(ReadOnly(GetConnectionString(sp))));
        services.AddScoped<IReadDb>(sp => sp.GetRequiredService<ReadDbContext>());

        services.AddScoped<ICommandHandler<CreateProductCommand, Guid>, CreateProductHandler>();
        services.AddScoped<IQueryHandler<GetProductsQuery, ProductDto[]>, GetProductsHandler>();
        services.AddScoped<IQueryHandler<GetProductByIdQuery, ProductDto?>, GetProductByIdHandler>();

        return services;
    }

    private static string? GetConnectionString(IServiceProvider sp) =>
        sp.GetRequiredService<IConfiguration>().GetConnectionString("Default");

    /// <summary>
    /// The read side's connection opens a read-only session: Postgres rejects every write from it
    /// (ExecuteDelete/ExecuteUpdate, raw SQL), not just the ones that go through SaveChanges.
    /// </summary>
    private static string? ReadOnly(string? connectionString)
    {
        if (connectionString is null)
        {
            return null;
        }

        const string readOnlySession = "-c default_transaction_read_only=on";
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Options = string.IsNullOrWhiteSpace(builder.Options) ? readOnlySession : $"{builder.Options} {readOnlySession}";
        return builder.ConnectionString;
    }
}
