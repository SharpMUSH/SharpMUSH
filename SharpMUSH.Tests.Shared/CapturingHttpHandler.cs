using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace SharpMUSH.Tests.Shared;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that records every request (and its body, read before the
/// client disposes it) and answers each with the same canned response.
/// </summary>
public sealed class CapturingHttpHandler : HttpMessageHandler
{
	private readonly Func<HttpResponseMessage> _respond;

	public CapturingHttpHandler(Func<HttpResponseMessage> respond) => _respond = respond;

	/// <summary>Answers <paramref name="status"/> with <paramref name="json"/> as an <c>application/json</c> body.</summary>
	public static CapturingHttpHandler WithJson(HttpStatusCode status, string? json = null) =>
		new(() => new HttpResponseMessage(status)
		{
			Content = new StringContent(json ?? string.Empty, Encoding.UTF8, "application/json")
		});

	/// <summary>Answers <paramref name="status"/> with <paramref name="body"/> serialised, or no content when it is null.</summary>
	public static CapturingHttpHandler WithBody(HttpStatusCode status, object? body) =>
		new(() => new HttpResponseMessage(status)
		{
			Content = body is null ? null : JsonContent.Create(body)
		});

	public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

	public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1].Request;

	public string? LastBody => Requests.Count == 0 ? null : Requests[^1].Body;

	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
		Requests.Add((request, body));
		return _respond();
	}
}
