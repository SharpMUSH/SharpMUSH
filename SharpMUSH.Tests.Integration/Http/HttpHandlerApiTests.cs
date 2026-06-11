using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Net;

namespace SharpMUSH.Tests.Integration.Http;

/// <summary>
/// End-to-end tests for the command-based inbound HTTP handler (help sharphttp): a request to
/// <c>/http/&lt;path&gt;</c> runs the http_handler's (#4) <c>&lt;METHOD&gt;</c> attribute as a command list —
/// PennMUSH's invisible-login + <c>@include #handler/&lt;method&gt;</c>. <c>%0</c> is the path+query,
/// <c>%1</c> the body, headers arrive as <c>%q&lt;hdr.name&gt;</c> registers, everything emitted to the
/// handler becomes the response body, and <c>@respond</c> shapes status/content-type/headers.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class HttpHandlerApiTests(ServerWebAppFactory factory)
{
	/// <summary>Seeds (or overwrites) a method attribute on the configured handler (#4), as God.</summary>
	private async Task SeedHandlerAttribute(string method, string commandList)
	{
		var mediator = factory.Services.GetRequiredService<IMediator>();
		var attributeService = factory.Services.GetRequiredService<IAttributeService>();

		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1, null)))).Known;
		var handler = (await mediator.Send(new GetObjectNodeQuery(new DBRef(4, null)))).Known;

		var result = await attributeService.SetAttributeAsync(god, handler, method, MModule.single(commandList));
		await Assert.That(result.IsT0).IsTrue();
	}

	[Test]
	public async Task Get_RunsHandlerAttribute_BodyAndRespondStatus()
	{
		// The milestone: think output becomes the body; @respond sets the status line. The status
		// text is unquoted — the PennMUSH oracle rejects `@respond 200 "TEST RESPONSE"` because
		// the character after the space must be alphanumeric (and responds `HTTP/1.1 200 TEST
		// RESPONSE` for the unquoted form).
		await SeedHandlerAttribute("GET", "think %0|%1; @respond 200 TEST RESPONSE");

		var http = factory.CreateHttpClient();
		var response = await http.GetAsync("http/foo?bar=baz");
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		// %0 = path including query string; %1 = empty body, so the think line ends with the pipe.
		// Oracle-verified PennMUSH body for this softcode: "/foo?bar=baz|".
		await Assert.That(body).Contains("/foo?bar=baz|");
		// @respond's status text travels up as the reason phrase.
		await Assert.That(response.ReasonPhrase ?? string.Empty).Contains("TEST RESPONSE");
		// Penn default content type unless @respond/type overrides it.
		await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/plain");
	}

	[Test]
	public async Task Post_BodyArrivesAsArgOne()
	{
		await SeedHandlerAttribute("POST", "think got=[%1]");

		var http = factory.CreateHttpClient();
		var response = await http.PostAsync("http/submit",
			new StringContent("""{"channel":"public","msg":"hi"}""", System.Text.Encoding.UTF8, "application/json"));
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		// The [ ] around %1 are MUSH evaluation brackets — they evaluate away; the JSON body
		// itself passes through verbatim.
		await Assert.That(body).Contains("""got={"channel":"public","msg":"hi"}""");
	}

	[Test]
	public async Task Put_HeadersArriveAsQRegisters()
	{
		// %q<hdr.NAME> per header plus %q<headers> listing the names (help sharphttp / HTTP3).
		await SeedHandlerAttribute("PUT", "think host=[%q<hdr.host>] custom=[%q<hdr.x-sharp-test>] names=[%q<headers>]");

		var http = factory.CreateHttpClient();
		using var request = new HttpRequestMessage(HttpMethod.Put, "http/headers");
		request.Headers.Add("X-Sharp-Test", "marker-value");
		request.Content = new StringContent(string.Empty);
		var response = await http.SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		// The [ ] are MUSH evaluation brackets and evaluate away; the register values remain.
		await Assert.That(body).Contains("custom=marker-value");
		await Assert.That(body).Contains("X-SHARP-TEST");
		// Host is always present on an HTTP/1.1 request.
		await Assert.That(body).Contains("host=localhost");
	}

	[Test]
	public async Task RespondTypeAndHeader_ShapeTheResponse()
	{
		await SeedHandlerAttribute("PATCH",
			"@respond/type application/json; @respond/header X-Powered-By=MUSHCode; think \\{\"ok\":true\\}");

		var http = factory.CreateHttpClient();
		using var request = new HttpRequestMessage(HttpMethod.Patch, "http/json");
		request.Content = new StringContent(string.Empty);
		var response = await http.SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That((int)response.StatusCode).IsEqualTo(200);
		await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
		await Assert.That(response.Headers.TryGetValues("X-Powered-By", out var values)).IsTrue();
		await Assert.That(values!.First()).IsEqualTo("MUSHCode");
		await Assert.That(body).Contains("\"ok\":true");
	}

	[Test]
	public async Task UnseededMethod_Returns404()
	{
		// No DELETE attribute on the handler: SharpMUSH answers 404 (deliberate deviation from
		// Penn's 200-empty; see help sharphttp HTTP2).
		var http = factory.CreateHttpClient();
		var response = await http.DeleteAsync("http/anything");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}
}
