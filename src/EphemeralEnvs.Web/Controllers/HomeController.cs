using System.Diagnostics;
using System.Text.Json;

using EphemeralEnvs.Contracts;
using EphemeralEnvs.Web.Models;
using EphemeralEnvs.Web.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement;

namespace EphemeralEnvs.Web.Controllers;

public sealed partial class HomeController(ProductsApiClient api, IFeatureManager features, ILogger<HomeController> logger) : Controller
{
    public const string ApiUnavailableMessage = "API is unavailable right now.";
    public const string StatsNotAvailableMessage = "Statistics are not available (feature off in the API).";
    public const string StatsUnavailableMessage = "Statistics are unavailable right now.";

    /// <summary>
    /// Product list fetched server-side. If the API fails, the page still renders with a friendly message.
    /// With the Web's ProductStats flag on, stats are fetched in parallel; their failure never hides the products.
    /// </summary>
    public async Task<IActionResult> Index()
    {
        var aborted = HttpContext.RequestAborted;
        var statsTask = await features.IsEnabledAsync(FeatureFlags.ProductStats) ? LoadStatsAsync(aborted) : null;
        try
        {
            var products = await api.GetProductsAsync(aborted);
            var (stats, statsMessage) = statsTask is null ? (null, null) : await statsTask;
            return View(new HomeIndexModel(products, statsTask is not null, stats, statsMessage));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !aborted.IsCancellationRequested)
        {
            // TaskCanceledException without an aborted request = the client's timeout.
            LogApiUnavailable(ex);
            ViewData["Error"] = ApiUnavailableMessage;
            return View(new HomeIndexModel([], false, null, null));
        }
    }

    /// <summary>404 = the API's flag is off (situation E); any other failure = unavailable. An aborted request propagates.</summary>
    private async Task<(ProductStatsDto? Stats, string? Message)> LoadStatsAsync(CancellationToken aborted)
    {
        try
        {
            var stats = await api.GetStatsAsync(aborted);
            return stats is null ? (null, StatsNotAvailableMessage) : (stats, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !aborted.IsCancellationRequested)
        {
            LogStatsUnavailable(ex);
            return (null, StatsUnavailableMessage);
        }
    }

    /// <summary>The same list plus a form, rendered by wwwroot/js/widget.js from the browser.</summary>
    public IActionResult Widget() => View();

    /// <summary>
    /// Server-side form with an antiforgery token (validated globally). Unlike the widget, it depends on the
    /// Data Protection keys: with several Web replicas they must be shared (DataProtection__KeysPath).
    /// </summary>
    [HttpGet]
    public IActionResult Create() => View(new CreateProductRequest());

    [HttpPost]
    public async Task<IActionResult> Create(CreateProductRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ModelState.IsValid)
        {
            return View(request);
        }

        var aborted = HttpContext.RequestAborted;
        try
        {
            var result = await api.CreateProductAsync(request, aborted);
            if (result.Created)
            {
                return RedirectToAction(nameof(Index));
            }

            foreach (var (field, messages) in result.Errors)
            {
                foreach (var message in messages)
                {
                    ModelState.AddModelError(field, message);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !aborted.IsCancellationRequested)
        {
            LogApiUnavailable(ex);
            ViewData["Error"] = ApiUnavailableMessage;
        }

        return View(request);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "API call failed; showing the page without products")]
    private partial void LogApiUnavailable(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stats call failed; showing the products without stats")]
    private partial void LogStatsUnavailable(Exception exception);
}
