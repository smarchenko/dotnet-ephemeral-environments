using System.Reflection;

namespace EphemeralEnvs.ArchitectureTests;

/// <summary>The solution's assemblies, located through one public type of each.</summary>
internal static class Assemblies
{
    public static readonly Assembly Api = typeof(Api.Controllers.ProductsController).Assembly;
    public static readonly Assembly Application = typeof(Application.DependencyInjection).Assembly;
    public static readonly Assembly Contracts = typeof(Contracts.ProductDto).Assembly;
    public static readonly Assembly Data = typeof(Data.AppDbContext).Assembly;
    public static readonly Assembly Domain = typeof(Domain.Product).Assembly;
    public static readonly Assembly ServiceDefaults = typeof(ServiceDefaults.ServiceDefaultsExtensions).Assembly;
    public static readonly Assembly Web = typeof(Web.Controllers.HomeController).Assembly;
}

/// <summary>Namespaces and type names used in the rules.</summary>
internal static class Ns
{
    public const string Api = "EphemeralEnvs.Api";
    public const string Application = "EphemeralEnvs.Application";
    public const string Contracts = "EphemeralEnvs.Contracts";
    public const string Data = "EphemeralEnvs.Data";
    public const string Domain = "EphemeralEnvs.Domain";
    public const string Web = "EphemeralEnvs.Web";

    public const string AspNetCore = "Microsoft.AspNetCore";
    public const string EntityFrameworkCore = "Microsoft.EntityFrameworkCore";
    public const string Npgsql = "Npgsql";

    public const string AppDbContext = "EphemeralEnvs.Data.AppDbContext";
    public const string ReadDbContext = "EphemeralEnvs.Application.Persistence.ReadDbContext";
    public const string IReadDb = "EphemeralEnvs.Application.Persistence.IReadDb";
}
