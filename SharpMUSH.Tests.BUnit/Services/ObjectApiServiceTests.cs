using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// That <see cref="ObjectApiService"/> puts an attribute value on the wire unmodified.
///
/// This is the seam that used to mangle: its predecessor,
/// <c>MushQueryService.SetAttributeAsync</c>, rewrote every newline as the two characters
/// <c>%r</c> so the value would survive as one line on the terminal WebSocket, and the editor
/// translated <c>%r</c> back to a newline on load. Together those made a real newline and a typed
/// <c>%r</c> indistinguishable. A JSON body has no line limit, so neither conversion is needed —
/// and these tests fail if either is reintroduced.
/// </summary>
public class ObjectApiServiceTests
{
	/// <summary>Captures the one request the service makes and replies with a canned response.</summary>
	private sealed class CapturingHandler(HttpStatusCode status, string? responseJson = null) : HttpMessageHandler
	{
		public HttpRequestMessage? Request { get; private set; }
		public string? RequestBody { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Request = request;
			RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

			return new HttpResponseMessage(status)
			{
				Content = new StringContent(responseJson ?? string.Empty, Encoding.UTF8, "application/json")
			};
		}
	}

	private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) =>
			new(handler, disposeHandler: false) { BaseAddress = new Uri("https://localhost/") };
	}

	private static (ObjectApiService Service, CapturingHandler Handler) Build(
		HttpStatusCode status = HttpStatusCode.NoContent, string? responseJson = null)
	{
		var handler = new CapturingHandler(status, responseJson);
		return (new ObjectApiService(new SingleClientFactory(handler)), handler);
	}

	[Test]
	public async Task SetAttribute_SendsTheValueVerbatim_NewlinesIntact()
	{
		var (service, handler) = Build();
		const string value = "$greet *:@pemit %#=Hi;\n  @pemit %#=Bye";

		await service.SetAttributeAsync(7, "CMD_GREET", value);

		var sent = JsonDocument.Parse(handler.RequestBody!).RootElement.GetProperty("value").GetString();

		await Assert.That(sent).IsEqualTo(value);
	}

	[Test]
	public async Task SetAttribute_DoesNotConvertNewlinesToPercentR()
	{
		var (service, handler) = Build();

		await service.SetAttributeAsync(7, "DESC", "line one\nline two");

		await Assert.That(handler.RequestBody).DoesNotContain("%r")
			.Because("the %r rewrite is exactly the behaviour this service replaced");
	}

	[Test]
	public async Task SetAttribute_LeavesATypedPercentRAlone()
	{
		var (service, handler) = Build();

		await service.SetAttributeAsync(7, "DESC", "line one%rline two");

		var sent = JsonDocument.Parse(handler.RequestBody!).RootElement.GetProperty("value").GetString();

		await Assert.That(sent).IsEqualTo("line one%rline two");
		await Assert.That(sent).DoesNotContain("\n")
			.Because("a %r the author typed is content, not formatting to expand");
	}

	[Test]
	public async Task SetAttribute_UsesPutOnTheAttributeRoute()
	{
		var (service, handler) = Build();

		await service.SetAttributeAsync(42, "BRANCH`LEAF", "x");

		await Assert.That(handler.Request!.Method).IsEqualTo(HttpMethod.Put);
		await Assert.That(handler.Request.RequestUri!.AbsolutePath)
			.IsEqualTo("/api/objects/42/attributes/BRANCH%60LEAF");
	}

	[Test]
	public async Task SetAttribute_OnRefusal_ReturnsTheServersMessage()
	{
		var (service, _) = Build(
			HttpStatusCode.Forbidden, """{"error":"You do not have permission to do that."}""");

		var error = await service.SetAttributeAsync(7, "PWNED", "nope");

		await Assert.That(error).IsEqualTo("You do not have permission to do that.");
	}

	[Test]
	public async Task SetAttribute_OnSuccess_ReturnsNoError()
	{
		var (service, _) = Build();

		await Assert.That(await service.SetAttributeAsync(7, "DESC", "fine")).IsNull();
	}

	[Test]
	public async Task GetAttributes_ReadsValuesVerbatim()
	{
		var (service, _) = Build(HttpStatusCode.OK,
			"""[{"name":"DESC","value":"line one\nline two","flags":[]}]""");

		var attributes = await service.GetAttributesAsync(7);

		await Assert.That(attributes[0].Value).IsEqualTo("line one\nline two")
			.Because("the load path must not translate either way round");
	}

	[Test]
	public async Task CreateObject_ReturnsTheDbrefNumber_StrippingAnyCreationTime()
	{
		var (service, _) = Build(HttpStatusCode.OK, """{"dbref":"#123:1700000000"}""");

		var (dbref, error) = await service.CreateObjectAsync("Widget", MushObjectType.Thing);

		await Assert.That(dbref).IsEqualTo(123);
		await Assert.That(error).IsNull();
	}

	[Test]
	public async Task CreateObject_OnRefusal_ReturnsTheMessageAndNoDbref()
	{
		var (service, _) = Build(HttpStatusCode.Forbidden, """{"error":"#-1 THAT IS A BAD NAME."}""");

		var (dbref, error) = await service.CreateObjectAsync("!!", MushObjectType.Thing);

		await Assert.That(dbref).IsNull();
		await Assert.That(error).IsEqualTo("#-1 THAT IS A BAD NAME.");
	}
}
