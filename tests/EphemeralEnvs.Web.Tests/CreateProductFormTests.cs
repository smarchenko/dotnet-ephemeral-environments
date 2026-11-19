using System.Net;
using System.Text;

using EphemeralEnvs.Web.Controllers;

using Microsoft.AspNetCore.Mvc.Testing;

namespace EphemeralEnvs.Web.Tests;

/// <summary>The server-side form /Home/Create: antiforgery, API call, errors shown in the form.</summary>
public sealed class CreateProductFormTests
{
    private static readonly WebApplicationFactoryClientOptions NoRedirects = new() { AllowAutoRedirect = false };

    [Fact]
    public async Task Get_Create_RendersFormWithToken()
    {
        await using var factory = new WebFactory();
        using var client = factory.CreateClient(NoRedirects);

        var html = await client.GetStringAsync(new Uri("/Home/Create", UriKind.Relative));

        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_WithoutToken_Returns400()
    {
        var api = Created();
        await using var factory = new WebFactory(apiHandler: api);
        using var client = factory.CreateClient(NoRedirects);

        using var response = await client.PostAsync(new Uri("/Home/Create", UriKind.Relative), Form("Tea", "4.20"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task Post_WithToken_Valid_CallsApi_RedirectsToIndex()
    {
        var api = Created();
        await using var factory = new WebFactory(apiHandler: api);
        using var client = factory.CreateClient(NoRedirects);
        var (token, _) = await WebFactory.GetFormTokenAsync(client);

        using var response = await client.PostAsync(new Uri("/Home/Create", UriKind.Relative), Form("Tea", "4.20", token));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        var call = Assert.Single(api.Requests);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("/products", call.Path);
        Assert.Contains("\"name\":\"Tea\"", call.Body, StringComparison.Ordinal);
        Assert.Contains("\"price\":4.20", call.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_ApiValidationError_ShowsFieldErrors()
    {
        var api = new StubApiHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"title":"One or more validation errors occurred.","status":400,"errors":{"Name":["A product with this name already exists."]}}""",
                Encoding.UTF8,
                "application/problem+json"),
        });
        await using var factory = new WebFactory(apiHandler: api);
        using var client = factory.CreateClient(NoRedirects);
        var (token, _) = await WebFactory.GetFormTokenAsync(client);

        using var response = await client.PostAsync(new Uri("/Home/Create", UriKind.Relative), Form("Tea", "4.20", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("A product with this name already exists.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_ApiUnavailable_ShowsFriendlyMessage()
    {
        await using var factory = new WebFactory(apiHandler: StubApiHandler.Unreachable());
        using var client = factory.CreateClient(NoRedirects);
        var (token, _) = await WebFactory.GetFormTokenAsync(client);

        using var response = await client.PostAsync(new Uri("/Home/Create", UriKind.Relative), Form("Tea", "4.20", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(HomeController.ApiUnavailableMessage, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_InvalidLocally_ShowsErrors_WithoutCallingApi()
    {
        var api = Created();
        await using var factory = new WebFactory(apiHandler: api);
        using var client = factory.CreateClient(NoRedirects);
        var (token, _) = await WebFactory.GetFormTokenAsync(client);

        using var response = await client.PostAsync(new Uri("/Home/Create", UriKind.Relative), Form("   ", "4.20", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("field-validation-error", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(api.Requests);
    }

    internal static StubApiHandler Created() => new(_ => new HttpResponseMessage(HttpStatusCode.Created)
    {
        Content = new StringContent(
            """{"id":"0196a000-0000-7000-8000-000000000099","name":"Tea","price":4.20,"createdAt":"2026-11-18T10:00:00Z"}""",
            Encoding.UTF8,
            "application/json"),
    });

    internal static FormUrlEncodedContent Form(string name, string price, string? token = null)
    {
        var fields = new Dictionary<string, string> { ["Name"] = name, ["Price"] = price };
        if (token is not null)
        {
            fields["__RequestVerificationToken"] = token;
        }

        return new FormUrlEncodedContent(fields);
    }
}
