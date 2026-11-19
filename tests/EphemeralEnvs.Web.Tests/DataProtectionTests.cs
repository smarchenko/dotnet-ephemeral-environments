using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;

namespace EphemeralEnvs.Web.Tests;

/// <summary>
/// Two Web instances = two replicas behind Traefik. The antiforgery token and cookie are encrypted with the
/// Data Protection keys, so a form rendered by one replica can be posted to another only when they share the keys
/// (<c>DataProtection:KeysPath</c>).
/// </summary>
public sealed class DataProtectionTests : IDisposable
{
    private readonly List<string> _directories = [];

    [Fact]
    public async Task TokenFromOneInstance_AcceptedByAnother_WhenKeysShared()
    {
        var keys = NewKeysDirectory();

        var status = await PostFormFromAToB(keys, keys);

        Assert.Equal(HttpStatusCode.Redirect, status);
    }

    [Fact]
    public async Task TokenFromOneInstance_RejectedByAnother_WhenKeysNotShared()
    {
        var status = await PostFormFromAToB(NewKeysDirectory(), NewKeysDirectory());

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task KeysAreWrittenToKeysPath()
    {
        var keys = NewKeysDirectory();
        await using var factory = new WebFactory(KeysPath(keys));
        using var client = factory.CreateClient();

        await WebFactory.GetFormTokenAsync(client);

        Assert.NotEmpty(Directory.GetFiles(keys, "key-*.xml"));
    }

    [Fact]
    public async Task Footer_ShowsInstanceName()
    {
        await using var factory = new WebFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains(Environment.MachineName, html, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (var directory in _directories)
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<HttpStatusCode> PostFormFromAToB(string keysOfA, string keysOfB)
    {
        await using var a = new WebFactory(KeysPath(keysOfA), CreateProductFormTests.Created());
        await using var b = new WebFactory(KeysPath(keysOfB), CreateProductFormTests.Created());
        using var clientA = a.CreateClient();
        using var clientB = b.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        var (token, cookie) = await WebFactory.GetFormTokenAsync(clientA);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Home/Create")
        {
            Content = CreateProductFormTests.Form("Tea", "4.20", token),
        };
        request.Headers.Add("Cookie", cookie);

        using var response = await clientB.SendAsync(request);
        return response.StatusCode;
    }

    private static Dictionary<string, string?> KeysPath(string directory) => new() { ["DataProtection:KeysPath"] = directory };

    private string NewKeysDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("ee-dp-keys-").FullName;
        _directories.Add(directory);
        return directory;
    }
}
