using System.Net;

namespace SalahWidget.Tests;

/// <summary>HttpMessageHandler that answers from a routing function and records every request URI.</summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly Func<Uri, HttpResponseMessage> _respond;

    public FakeHttp(Func<Uri, HttpResponseMessage> respond) => _respond = respond;

    public List<Uri> Requests { get; } = new();

    public HttpClient Client() => new(this);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(_respond(request.RequestUri!));
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Fail(HttpStatusCode status = HttpStatusCode.InternalServerError) =>
        new(status) { Content = new StringContent("") };

    public static HttpResponseMessage Throw() => throw new HttpRequestException("network down");
}

/// <summary>A unique temp file path that is deleted when disposed.</summary>
internal sealed class TempFile : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"salah-test-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { File.Delete(Path); } catch (IOException) { }
    }
}
