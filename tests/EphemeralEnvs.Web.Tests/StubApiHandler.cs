namespace EphemeralEnvs.Web.Tests;

/// <summary>Stands in for the API: answers every request with <paramref name="respond"/> and records what it saw.</summary>
public sealed class StubApiHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];

    /// <summary>Behaves like an API that cannot be reached.</summary>
    public static StubApiHandler Unreachable() => new(_ => throw new HttpRequestException("Connection refused"));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
        return respond(request);
    }
}
