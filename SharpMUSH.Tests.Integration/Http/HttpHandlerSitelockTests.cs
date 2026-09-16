using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Net;

namespace SharpMUSH.Tests.Integration.Http;

/// <summary>
/// Issue #1122: the address the <c>/http/</c> route hands to the dispatcher is the one the
/// forwarded-headers pipeline resolved, and sitelock rules for the http_handler decide on it
/// before any softcode runs (help sharphttp, "HTTP SITELOCK"; PennMUSH src/bsd.c:3814-3839).
///
/// The shared test host trusts no proxies (<c>ForwardedHeaders:KnownProxies</c> is empty), which is
/// exactly the spoof-resistant production default — so an <c>X-Forwarded-For</c> a caller writes
/// itself must not become the address policy decides on. As in <c>SitelockCheckTests</c>, the rule
/// under test targets whatever this TestServer actually resolves to rather than a guessed literal.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class HttpHandlerSitelockTests(ServerWebAppFactory factory)
{
	/// <summary>An address that is never this TestServer's client (TEST-NET-3, RFC 5737).</summary>
	private const string SpoofedAddress = "203.0.113.77";

	private async Task SeedHandlerAttribute(string method, string commandList)
	{
		var mediator = factory.Services.GetRequiredService<IMediator>();
		var attributeService = factory.Services.GetRequiredService<IAttributeService>();

		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1, null)))).Expect<AnySharpObject>();
		var handler = (await mediator.Send(new GetObjectNodeQuery(new DBRef(8, null)))).Expect<AnySharpObject>();

		var result = await attributeService.SetAttributeAsync(god, handler, method, MarkupText.Plain(commandList));
		await Assert.That(result.Value).IsTypeOf<Success>();
	}

	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	/// <summary>
	/// The address this client resolves to server-side, mapped through the same fallback the auth
	/// surfaces apply — this in-process TestServer reports no RemoteIpAddress, so "unknown" is what
	/// actually reaches a policy check.
	/// </summary>
	private static async Task<string> ClientAddressAsync(HttpClient http)
	{
		using var response = await http.GetAsync("api/debug/client-ip");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var raw = await response.Content.ReadAsStringAsync();
		return string.IsNullOrEmpty(raw) ? "unknown" : raw;
	}

	private static IDisposable Sitelock(params (string Pattern, string[] Flags)[] rules) =>
		TestOptionsOverride.Scope(options => options with
		{
			SitelockRules = new SitelockRulesOptions(
				rules.ToDictionary(rule => rule.Pattern, rule => rule.Flags))
		});

	private static async Task<HttpResponseMessage> SendAsync(HttpClient http, string method, string path, string? forwardedFor = null)
	{
		using var request = new HttpRequestMessage(new HttpMethod(method), path);
		if (forwardedFor is not null)
		{
			request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
		}

		return await http.SendAsync(request);
	}

	[Test]
	public async Task ASitelockedAddressIsRefusedAndTheHandlerNeverRuns()
	{
		await SeedHandlerAttribute("LOCKED", "think handler-output");

		var http = CreateClient();
		var address = await ClientAddressAsync(http);

		using (Sitelock((address, ["!connect"])))
		{
			using var response = await SendAsync(http, "LOCKED", "http/locked");
			var body = await response.Content.ReadAsStringAsync();

			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
			await Assert.That(body).DoesNotContain("handler-output");
		}

		// Same request, same handler, no rule: the refusal was the policy and nothing else.
		using var allowed = await SendAsync(http, "LOCKED", "http/locked");
		await Assert.That(allowed.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(await allowed.Content.ReadAsStringAsync()).Contains("handler-output");
	}

	[Test]
	public async Task PathRulesGateOneRouteWithoutClosingTheRest()
	{
		await SeedHandlerAttribute("PATHLOCK", "think handler-output");

		var http = CreateClient();

		using (Sitelock(("*`PATHLOCK`/admin/*", ["!connect"])))
		{
			using var refused = await SendAsync(http, "PATHLOCK", "http/admin/secrets");
			await Assert.That(refused.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

			using var served = await SendAsync(http, "PATHLOCK", "http/public/notes");
			await Assert.That(served.StatusCode).IsEqualTo(HttpStatusCode.OK);
		}
	}

	[Test]
	public async Task ThePathReachesTheApplicationPercentDecoded()
	{
		// Why SafeLogValue exists. Routing decodes the catch-all route value, so "%0A" in a request
		// target is a real newline in the string the server then hands to softcode — and would hand
		// to a log call. If this ever stops being true the sanitiser is no longer load-bearing; while
		// it stays true, nothing may log this value raw.
		await SeedHandlerAttribute("DECODED", "think %0");

		var http = CreateClient();
		using var response = await SendAsync(http, "DECODED", "http/foo%0Abar%0Dbaz");
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(body).Contains("/foo\nbar\rbaz");
	}

	[Test]
	public async Task AForwardedHeaderFromAnUntrustedCallerCannotChooseThePolicyAddress()
	{
		await SeedHandlerAttribute("SPOOF", "think handler-output");

		var http = CreateClient();
		var address = await ClientAddressAsync(http);

		// Claiming to be an address nobody locked must not buy passage: the real address is still
		// what the rule is matched against.
		using (Sitelock((address, ["!connect"])))
		{
			using var response = await SendAsync(http, "SPOOF", "http/spoof", forwardedFor: SpoofedAddress);
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
		}

		// And the mirror image: a rule against the claimed address must not refuse a caller who
		// merely asserted it in a header the host does not trust.
		using (Sitelock((SpoofedAddress, ["!connect"])))
		{
			using var response = await SendAsync(http, "SPOOF", "http/spoof", forwardedFor: SpoofedAddress);
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		}
	}
}
