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

	[Test]
	public async Task OverTheConfiguredQuota_Answers429()
	{
		await SeedHandlerAttribute("QUOTA", "think served");

		using var scope = PerSecond(3);
		var http = factory.CreateHttpClient();

		// The bucket starts at its ceiling, so exactly `http_per_second` requests are served before
		// the next one is refused. Issued back to back, so refill (3 permits/second) cannot cover
		// the fourth.
		var statuses = new List<HttpStatusCode>();
		for (var i = 0; i < 4; i++)
		{
			statuses.Add(await SendAsync(http, "QUOTA", "http/quota"));
		}

		await Assert.That(statuses[0]).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(statuses[1]).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(statuses[2]).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(statuses[3]).IsEqualTo(HttpStatusCode.TooManyRequests);
	}

	[Test]
	public async Task RaisingTheLimitTakesEffectOnTheNextRequest()
	{
		await SeedHandlerAttribute("QUOTALIVE", "think served");

		var http = factory.CreateHttpClient();

		using (PerSecond(1))
		{
			await Assert.That(await SendAsync(http, "QUOTALIVE", "http/quota-live")).IsEqualTo(HttpStatusCode.OK);
			await Assert.That(await SendAsync(http, "QUOTALIVE", "http/quota-live")).IsEqualTo(HttpStatusCode.TooManyRequests);
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
