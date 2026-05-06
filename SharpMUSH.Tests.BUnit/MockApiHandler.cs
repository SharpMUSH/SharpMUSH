using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using NSubstitute;

namespace SharpMUSH.Tests.BUnit;

/// <summary>
/// A mock HTTP message handler that returns pre-configured responses based on URL path and method.
/// </summary>
public class MockApiHandler : HttpMessageHandler
{
	private readonly Dictionary<(string Method, string Path), Func<HttpRequestMessage, HttpResponseMessage>> _handlers = new();

	public MockApiHandler OnGet(string path, object responseBody) =>
		On("GET", path, _ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = JsonContent.Create(responseBody)
		});

	public MockApiHandler OnPost(string path, HttpStatusCode status = HttpStatusCode.OK, object? responseBody = null) =>
		On("POST", path, _ => new HttpResponseMessage(status)
		{
			Content = responseBody != null
				? JsonContent.Create(responseBody)
				: new StringContent("")
		});

	public MockApiHandler OnPut(string path, HttpStatusCode status = HttpStatusCode.OK, object? responseBody = null) =>
		On("PUT", path, _ => new HttpResponseMessage(status)
		{
			Content = responseBody != null
				? JsonContent.Create(responseBody)
				: new StringContent("")
		});

	public MockApiHandler OnDelete(string path, HttpStatusCode status = HttpStatusCode.OK) =>
		On("DELETE", path, _ => new HttpResponseMessage(status));

	public MockApiHandler On(string method, string path,
		Func<HttpRequestMessage, HttpResponseMessage> handler)
	{
		_handlers[(method.ToUpperInvariant(), path)] = handler;
		return this;
	}

	/// <summary>
	/// Return 404 for any path matching a prefix.
	/// </summary>
	public MockApiHandler OnAny(HttpStatusCode status = HttpStatusCode.NotFound) =>
		On("*", "*", _ => new HttpResponseMessage(status));

	protected override Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var method = request.Method.Method.ToUpperInvariant();
		var path = request.RequestUri?.AbsolutePath ?? "";

		if (_handlers.TryGetValue((method, path), out var handler))
			return Task.FromResult(handler(request));

		if (_handlers.TryGetValue(("*", "*"), out var fallback))
			return Task.FromResult(fallback(request));

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}

	public HttpClient CreateClient() =>
		new(this) { BaseAddress = new Uri("http://localhost/") };

	public IHttpClientFactory CreateFactory()
	{
		var factory = NSubstitute.Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(CreateClient());
		return factory;
	}
}
