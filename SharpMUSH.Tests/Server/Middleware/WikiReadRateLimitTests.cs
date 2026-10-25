using System.Net;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.RateLimiting;

namespace SharpMUSH.Tests.Server.Middleware;

/// <summary>
/// The <c>"wiki-read"</c> policy (<see cref="WikiReadRateLimit"/>), registered as the server registers it, on an
/// in-process minimal app. <c>X-Test-Client</c> stands in for the forwarded client address and <c>X-Test-User</c>
/// for a signed-in account.
/// </summary>
public class WikiReadRateLimitTests
{
	private const string ClientHeader = "X-Test-Client";
	private const string UserHeader = "X-Test-User";

	private static async Task<(WebApplication App, HttpClient Client)> StartAsync(int permitLimit)
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Logging.ClearProviders();
		builder.Logging.AddSerilog(TestDiagnostics.CreateLogger(), dispose: true);

		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["RateLimiting:WikiRead:PermitLimit"] = permitLimit.ToString(),
				["RateLimiting:WikiRead:WindowSeconds"] = "60",
			})
			.Build();

		builder.Services.AddRateLimiter(opts =>
		{
			opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
			opts.AddWikiReadPolicy(configuration);
		});

		var app = builder.Build();
		app.Use((context, next) =>
		{
			if (IPAddress.TryParse(context.Request.Headers[ClientHeader], out var address))
				context.Connection.RemoteIpAddress = address;
			if (context.Request.Headers.ContainsKey(UserHeader))
				context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "reader")], "Test"));
			return next(context);
		});
		app.UseRouting();
		app.UseRateLimiter();
		app.MapGet("/wiki", () => "ok").RequireRateLimiting(WikiReadRateLimit.PolicyName);

		await app.StartAsync();
		return (app, app.GetTestClient());
	}

	private static async Task<HttpStatusCode> GetAsync(HttpClient client, string clientIp, bool signedIn = false)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, "/wiki");
		request.Headers.Add(ClientHeader, clientIp);
		if (signedIn) request.Headers.Add(UserHeader, "1");
		using var response = await client.SendAsync(request);
		return response.StatusCode;
	}

	[Test]
	public async Task AnAnonymousReader_PastTheLimit_Is429()
	{
		var (app, client) = await StartAsync(permitLimit: 2);
		await using var _ = app;

		await Assert.That(await GetAsync(client, "203.0.113.1")).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(await GetAsync(client, "203.0.113.1")).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(await GetAsync(client, "203.0.113.1")).IsEqualTo(HttpStatusCode.TooManyRequests);

		await Assert.That(await GetAsync(client, "198.51.100.7")).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task ASignedInReader_IsNotLimited()
	{
		var (app, client) = await StartAsync(permitLimit: 1);
		await using var _ = app;

		for (var i = 0; i < 5; i++)
			await Assert.That(await GetAsync(client, "203.0.113.2", signedIn: true)).IsEqualTo(HttpStatusCode.OK);

		// Signing in elsewhere on the address spends nothing of the anonymous window.
		await Assert.That(await GetAsync(client, "203.0.113.2")).IsEqualTo(HttpStatusCode.OK);
	}

	/// <summary>
	/// The policy sits on <see cref="WikiControllerBase"/>, so a wiki controller added later is under it without
	/// saying so. MVC reads controller attributes with inheritance, as this does.
	/// </summary>
	[Test]
	public async Task EveryWikiController_AndTheSitemap_AreUnderThePolicy()
	{
		var controllers = typeof(WikiControllerBase).Assembly.GetTypes()
			.Where(type => type is { IsAbstract: false } && type.IsSubclassOf(typeof(WikiControllerBase)))
			.Append(typeof(SeoController))
			.ToList();

		await Assert.That(controllers.Count).IsGreaterThan(5);
		foreach (var controller in controllers)
		{
			await Assert.That(controller.GetCustomAttribute<EnableRateLimitingAttribute>(inherit: true)?.PolicyName)
				.IsEqualTo(WikiReadRateLimit.PolicyName);
		}
	}
}
