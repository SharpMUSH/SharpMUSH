using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Services;
using System.Net;
using System.Text;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// Unit tests for <see cref="ProfileService"/>'s error-path resilience: a misconfigured in-game
/// http_handler (returning a 502 envelope, or — defensively — a 200 with a non-JSON body) must
/// degrade to <c>null</c> so the Character Page shows "Profile unavailable" instead of crashing the
/// Blazor renderer with an unhandled <see cref="System.Text.Json.JsonException"/>.
///
/// The server side of the same contract is covered by ProfileControllerTests.
/// </summary>
public class ProfileServiceTests
{
	private sealed class CannedHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(statusCode)
			{
				Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
			});
	}

	private static ProfileService BuildService(HttpStatusCode code, string body)
	{
		var http = new HttpClient(new CannedHandler(code, body)) { BaseAddress = new Uri("http://localhost") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);
		return new ProfileService(factory, Substitute.For<ILogger<ProfileService>>());
	}

	[Test]
	public async Task GetSchemaAsync_ValidJson_ReturnsSchema()
	{
		var svc = BuildService(HttpStatusCode.OK, """{"sections":[{"name":"Demographics","order":1,"fields":[]}]}""");

		var schema = await svc.GetSchemaAsync();

		await Assert.That(schema).IsNotNull();
		await Assert.That(schema!.Sections!.Count).IsEqualTo(1);
	}

	[Test]
	public async Task GetSchemaAsync_HandlerError502_ReturnsNullNotThrow()
	{
		// ProfileController returns 502 when the handler produced invalid output.
		var svc = BuildService((HttpStatusCode)502, """{"status":502,"error":"...","detail":"#-1 ..."}""");

		var schema = await svc.GetSchemaAsync();

		await Assert.That(schema).IsNull();
	}

	[Test]
	public async Task GetSchemaAsync_EmptyOkBody_ReturnsNullNotThrow()
	{
		// Defensive: a 200 with an empty body (the original crash) must be swallowed as JsonException.
		var svc = BuildService(HttpStatusCode.OK, string.Empty);

		var schema = await svc.GetSchemaAsync();

		await Assert.That(schema).IsNull();
	}

	[Test]
	public async Task GetProfileAsync_HandlerError502_ReturnsNullNotThrow()
	{
		var svc = BuildService((HttpStatusCode)502, """{"status":502,"error":"...","detail":""}""");

		var data = await svc.GetProfileAsync("God");

		await Assert.That(data).IsNull();
	}
}
