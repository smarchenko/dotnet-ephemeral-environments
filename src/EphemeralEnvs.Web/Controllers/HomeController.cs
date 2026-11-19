using System.Diagnostics;

using EphemeralEnvs.Contracts;
using EphemeralEnvs.Web.Models;
using EphemeralEnvs.Web.Services;

using Microsoft.AspNetCore.Mvc;

namespace EphemeralEnvs.Web.Controllers;

public sealed partial class HomeController(ProductsApiClient api, ILogger<HomeController> logger) : Controller
{
    public const string ApiUnavailableMessage = "API is unavailable right now.";

    /// <summary>Product list fetched server-side. If the API fails, the page still renders with a friendly message.</summary>
    public async Task<IActionResult> Index()
    {
        var aborted = HttpContext.RequestAborted;
        try
        {
            return View(await api.GetProductsAsync(aborted));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !aborted.IsCancellationRequested)
        {
            // TaskCanceledException without an aborted request = the client's timeout.
            LogApiUnavailable(ex);
            ViewData["Error"] = ApiUnavailableMessage;
            return View(Array.Empty<ProductDto>());
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
}
