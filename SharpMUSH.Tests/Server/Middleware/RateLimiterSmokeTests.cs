using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
/// The production <c>"public-api"</c> policy (<see cref="PublicApiRateLimit"/>), registered exactly as
/// the server registers it, on an in-process minimal app (no Docker). The caller's address is taken
/// from an <c>X-Test-Client</c> header by a middleware standing in for UseForwardedHeaders, so one
/// test can be several clients.
/// </summary>
public class RateLimiterSmokeTests
{
	private const string ClientHeader = "X-Test-Client";

	private static async Task<(WebApplication App, HttpClient Client)> StartAsync(int permitLimit, int queueLimit = 0)
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Logging.ClearProviders();
		builder.Logging.AddSerilog(TestDiagnostics.CreateLogger(), dispose: true);

		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["RateLimiting:PublicApi:PermitLimit"] = permitLimit.ToString(),
				["RateLimiting:PublicApi:WindowSeconds"] = "60",
				["RateLimiting:PublicApi:QueueLimit"] = queueLimit.ToString(),
			})
			.Build();

		builder.Services.AddRateLimiter(opts =>
		{
			opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
			opts.AddPublicApiPolicy(configuration);
		});

		var app = builder.Build();
		app.Use((context, next) =>
		{
			if (IPAddress.TryParse(context.Request.Headers[ClientHeader], out var address))
			{
				context.Connection.RemoteIpAddress = address;
			}

			return next(context);
		});
		app.UseRouting();
		app.UseRateLimiter();
		app.MapGet("/limited", () => "ok").RequireRateLimiting(PublicApiRateLimit.PolicyName);

		await app.StartAsync();
		return (app, app.GetTestClient());
	}

	private static async Task<HttpStatusCode> GetAsync(HttpClient client, string clientIp)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, "/limited");
		request.Headers.Add(ClientHeader, clientIp);
		using var response = await client.SendAsync(request);
		return response.StatusCode;
	}

	[Test]
	public async Task Requests_WithinTheConfiguredLimit_AllSucceed()
	{
		var (app, client) = await StartAsync(permitLimit: 3);
		await using var _ = app;

		for (var i = 0; i < 3; i++)
		{
			await Assert.That(await GetAsync(client, "203.0.113.1")).IsEqualTo(HttpStatusCode.OK);
		}
	}

	[Test]
	public async Task A_RequestPastTheConfiguredLimit_Is429()
	{
		var (app, client) = await StartAsync(permitLimit: 2);
		await using var _ = app;

		await GetAsync(client, "203.0.113.1");
		await GetAsync(client, "203.0.113.1");

		await Assert.That(await GetAsync(client, "203.0.113.1")).IsEqualTo(HttpStatusCode.TooManyRequests);
	}

	/// <summary>
	/// The policy used to be one window for every caller, so the site as a whole got the permit limit
	/// — and one client spending it locked every other client out of logging in. Each address now has
	/// a window of its own.
	/// </summary>
	[Test]
	public async Task A_ClientThatExhaustedItsWindow_DoesNotThrottleAnother()
	{
		var (app, client) = await StartAsync(permitLimit: 2);
		await using var _ = app;

		await GetAsync(client, "203.0.113.1");
		await GetAsync(client, "203.0.113.1");
		await Assert.That(await GetAsync(client, "203.0.113.1")).IsEqualTo(HttpStatusCode.TooManyRequests);

		await Assert.That(await GetAsync(client, "198.51.100.7")).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(await GetAsync(client, "198.51.100.7")).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task ThePartitionKey_IsTheRemoteAddress()
	{
		var context = new DefaultHttpContext();
		await Assert.That(PublicApiRateLimit.PartitionKey(context)).IsEqualTo(PublicApiRateLimit.UnknownClient);

		context.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8::1");
		await Assert.That(PublicApiRateLimit.PartitionKey(context)).IsEqualTo("2001:db8::1");
	}

	/// <summary>
	/// Every portal page load asks for these two before it renders. They are cheap reads, and under
	/// the limiter the portal's boot waited on — or was refused by — the login throttle.
	/// </summary>
	[Test]
	[Arguments(typeof(SetupController), nameof(SetupController.GetStatus))]
	[Arguments(typeof(ServerInfoController), nameof(ServerInfoController.Get))]
	public async Task ThePortalBootReads_AreNotRateLimited(Type controller, string action)
	{
		var method = controller.GetMethod(action)!;

		await Assert.That(method.GetCustomAttribute<EnableRateLimitingAttribute>()).IsNull();
		await Assert.That(controller.GetCustomAttribute<EnableRateLimitingAttribute>()).IsNull();
	}

	/// <summary>The endpoints the limiter exists for keep it.</summary>
	[Test]
	[Arguments(typeof(SetupController), nameof(SetupController.Complete))]
	[Arguments(typeof(AuthController), nameof(AuthController.GetMushToken))]
	public async Task TheCredentialEndpoints_KeepTheLimiter(Type controller, string action)
	{
		var method = controller.GetMethod(action)!;

		await Assert.That(method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
			.IsEqualTo(PublicApiRateLimit.PolicyName);
	}
}
