using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Services;
using System.Net;
using System.Text;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// Unit tests for <see cref="SchemaAppService"/>'s error-path resilience (Area 21): a misconfigured
/// in-game http_handler (returning a 502 envelope, or — defensively — a 200 with a non-JSON body)
/// must degrade schema/data fetches to <c>null</c> so a Dynamic Application / the Character Page
/// shows "unavailable" instead of crashing the Blazor renderer with an unhandled JsonException.
/// A failed action POST degrades to an <c>Ok=false</c> envelope with a <c>_global</c> error.
/// </summary>
public class SchemaAppServiceTests
{
	private sealed class CannedHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(statusCode)
			{
				Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
			});
	}

	private static SchemaAppService BuildService(HttpStatusCode code, string body)
	{
		var http = new HttpClient(new CannedHandler(code, body)) { BaseAddress = new Uri("http://localhost") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);
		return new SchemaAppService(factory, Substitute.For<ILogger<SchemaAppService>>());
	}

	[Test]
	public async Task GetSchemaAsync_ValidViewDocument_ReturnsParsedDocument()
	{
		var svc = BuildService(HttpStatusCode.OK, """
			{"kind":"view","schema_version":1,"pages":[{"key":"p1","order":1,"sections":[
			  {"name":"Demographics","order":1,"elements":[]}]}]}
			""");

		var doc = await svc.GetSchemaAsync("http/profile/schema");

		await Assert.That(doc).IsNotNull();
		await Assert.That(doc!.Kind).IsEqualTo("view");
		await Assert.That(doc.Pages!.Count).IsEqualTo(1);
		await Assert.That(doc.Pages![0].Sections!.Count).IsEqualTo(1);
	}

	[Test]
	public async Task GetSchemaAsync_HandlerError502_ReturnsNullNotThrow()
	{
		var svc = BuildService((HttpStatusCode)502, """{"status":502,"error":"...","detail":"#-1 ..."}""");

		await Assert.That(await svc.GetSchemaAsync("http/profile/schema")).IsNull();
	}

	[Test]
	public async Task GetSchemaAsync_EmptyOkBody_ReturnsNullNotThrow()
	{
		// Defensive: a 200 with an empty body (the original crash) must be swallowed as JsonException.
		var svc = BuildService(HttpStatusCode.OK, string.Empty);

		await Assert.That(await svc.GetSchemaAsync("http/profile/schema")).IsNull();
	}

	[Test]
	public async Task GetDataAsync_HandlerError502_ReturnsNullNotThrow()
	{
		var svc = BuildService((HttpStatusCode)502, """{"status":502,"error":"...","detail":""}""");

		await Assert.That(await svc.GetDataAsync("http/profile?objid=%231:1")).IsNull();
	}

	[Test]
	public async Task SubmitAsync_TransportSuccess_ReturnsEnvelope()
	{
		var svc = BuildService(HttpStatusCode.OK, """{"ok":false,"errors":{"name":"Required."}}""");

		var result = await svc.SubmitAsync("http/chargen/submit", new Dictionary<string, object?> { ["name"] = "" });

		await Assert.That(result.Ok).IsFalse();
		await Assert.That(result.Errors!["name"]).IsEqualTo("Required.");
	}

	[Test]
	public async Task SubmitAsync_NonJsonError_SynthesizesGlobalFailure()
	{
		var svc = BuildService(HttpStatusCode.InternalServerError, "boom (not json)");

		var result = await svc.SubmitAsync("http/chargen/submit", new Dictionary<string, object?>());

		await Assert.That(result.Ok).IsFalse();
		await Assert.That(result.Errors!.ContainsKey("_global")).IsTrue();
	}
}
