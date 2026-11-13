using EphemeralEnvs.ServiceDefaults;
using EphemeralEnvs.Web.Options;
using EphemeralEnvs.Web.Services;

using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// JSON logs with TraceId/SpanId (shared with the API; OpenTelemetry joins in stage O1).
builder.AddServiceDefaults();

// Both API URLs are required and absolute, the timeout positive; a bad value stops the app at startup, not on the first request.
builder.Services.AddOptions<ApiOptions>()
    .Bind(builder.Configuration.GetSection(ApiOptions.SectionName))
    .Validate(ApiOptions.IsValid, "Api:InternalBaseUrl and Api:PublicBaseUrl must be absolute URLs, Api:TimeoutSeconds > 0.")
    .ValidateOnStart();

builder.Services.AddHttpClient<ProductsApiClient>((sp, http) =>
{
    var options = sp.GetRequiredService<IOptions<ApiOptions>>().Value;
    var baseUrl = options.InternalBaseUrl!;
    // A trailing slash keeps a path prefix (http://host/api/) when relative URLs are resolved.
    http.BaseAddress = baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");
    http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
