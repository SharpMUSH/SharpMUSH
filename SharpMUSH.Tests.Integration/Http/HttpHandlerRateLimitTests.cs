using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;
using System.Net;

namespace SharpMUSH.Tests.Integration.Http;

/// <summary>
/// Issue #1121: <c>http_per_second</c> gates the softcode HTTP surface, as PennMUSH's
/// <c>http_quota</c> does (src/bsd.c 1008-1017, 3643-3648, 3741).
///
/// Penn keeps ONE global quota — not a per-IP one — accruing <c>http_per_second</c> permits a
/// second up to a burst ceiling of <c>http_per_second</c>, and spends one per handled request.
/// Below 1 the HTTP surface is off entirely ("No HTTPHandler", bsd.c:3741).
///
/// Each test scopes a limit no other test uses, because the limiter partitions on the configured
/// limit: draining the bucket here can never throttle a test running in parallel.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class HttpHandlerRateLimitTests(ServerWebAppFactory factory)
{
	/// <summary>Seeds (or overwrites) a method attribute on the configured handler (#8), as God.</summary>
	private async Task SeedHandlerAttribute(string method, string commandList)
	{
		var mediator = factory.Services.GetRequiredService<IMediator>();
		var attributeService = factory.Services.GetRequiredService<IAttributeService>();

		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1, null)))).Expect<AnySharpObject>();
		var handler = (await mediator.Send(new GetObjectNodeQuery(new DBRef(8, null)))).Expect<AnySharpObject>();

		var result = await attributeService.SetAttributeAsync(god, handler, method, MarkupText.Plain(commandList));
		await Assert.That(result.Value).IsTypeOf<Success>();
	}

	private static IDisposable PerSecond(uint limit) =>
		TestOptionsOverride.Scope(options => options with
		{
			Database = options.Database with { HttpRequestsPerSecond = limit }
		});

	private async Task<HttpStatusCode> SendAsync(HttpClient http, string method, string path)
	{
		using var request = new HttpRequestMessage(new HttpMethod(method), path);
		var response = await http.SendAsync(request);
		return response.StatusCode;
	}

	/// <summary>
	/// Sends <paramref name="count"/> requests at once and returns how many were served and refused.
	/// The bucket refills continuously (a limit of 3 earns a permit every 333ms), so requests sent one
	/// after another only exhaust it while each is faster than that: in a loaded run the fourth found a
	/// refilled permit and was served. Sent together, every request takes its permit (the limiter runs
	/// before the softcode does) within a few milliseconds. The exact accounting is pinned against a
	/// controlled clock in HttpQuotaRateLimiterTests; this proves the route is wired to it.
	/// </summary>
	private async Task<(int Served, int Refused)> SendTogether(HttpClient http, string method, string path, int count)
	{
		var statuses = await Task.WhenAll(Enumerable.Range(0, count).Select(_ => SendAsync(http, method, path)));

		await Assert.That(statuses.All(s => s is HttpStatusCode.OK or HttpStatusCode.TooManyRequests))
			.IsTrue().Because(string.Join(", ", statuses));

		return (statuses.Count(s => s == HttpStatusCode.OK), statuses.Count(s => s == HttpStatusCode.TooManyRequests));
	}

	[Test]
	public async Task OverTheConfiguredQuota_Answers429()
	{
		await SeedHandlerAttribute("QUOTA", "think served");

		using var scope = PerSecond(3);
		var http = factory.CreateHttpClient();

		// The bucket starts at its ceiling: `http_per_second` requests are served and the rest of a
		// burst is refused. Refill while the burst lands can only serve one or two more.
		var (served, refused) = await SendTogether(http, "QUOTA", "http/quota", 10);
		await Assert.That(served).IsGreaterThanOrEqualTo(3);
		await Assert.That(refused).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async Task RaisingTheLimitTakesEffectOnTheNextRequest()
	{
		await SeedHandlerAttribute("QUOTALIVE", "think served");

		var http = factory.CreateHttpClient();

		using (PerSecond(1))
		{
			var (served, refused) = await SendTogether(http, "QUOTALIVE", "http/quota-live", 5);
			await Assert.That(served).IsGreaterThanOrEqualTo(1);
			await Assert.That(refused).IsGreaterThanOrEqualTo(1);
		}

		// The option is read per request, so a raised limit admits traffic the old one refused —
		// no restart, and no leftover exhausted bucket from the lower setting.
		using (PerSecond(37))
		{
			for (var i = 0; i < 8; i++)
			{
				await Assert.That(await SendAsync(http, "QUOTALIVE", "http/quota-live")).IsEqualTo(HttpStatusCode.OK);
			}
		}
	}

	[Test]
	public async Task QuotaBelowOne_DisablesTheHttpSurface()
	{
		await SeedHandlerAttribute("QUOTAOFF", "think served");

		using var scope = PerSecond(0);
		var http = factory.CreateHttpClient();

		// Penn refuses the request outright when http_per_second < 1 (bsd.c:3741, reason
		// "No HTTPHandler"); SharpMUSH answers that the same way it answers an unset http_handler.
		await Assert.That(await SendAsync(http, "QUOTAOFF", "http/quota-off")).IsEqualTo(HttpStatusCode.NotFound);
	}
}
