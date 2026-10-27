using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Server.Middleware;

namespace SharpMUSH.Tests.Server.Middleware;

public class SecurityHeadersMiddlewareTests
{
	private static async Task<WebApplication> BuildAndStartAsync()
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.WebHost.UseTestServer();
		builder.Services.AddLogging(l => l.ClearProviders());

		var app = builder.Build();
		app.UseMiddleware<SecurityHeadersMiddleware>();
		app.Run(ctx =>
		{
			if (ctx.Request.Path.StartsWithSegments("/credentials"))
				ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
			if (ctx.Request.Path.StartsWithSegments("/http"))
				ctx.Response.Headers["X-Frame-Options"] = "DENY";
			return ctx.Response.WriteAsync("ok");
		});

		await app.StartAsync();
		return app;
	}

	private static async Task<HttpResponseMessage> GetAsync(WebApplication app, string path)
	{
		using var client = new HttpClient(app.GetTestServer().CreateHandler()) { BaseAddress = new Uri("http://localhost") };
		return await client.GetAsync(path);
	}

	private static string? Header(HttpResponseMessage response, string name)
		=> response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

	[Test]
	[Arguments("/")]
	[Arguments("/api/server-info")]
	[Arguments("/wiki/Main_Page")]
	public async Task PortalAndApiResponses_CarryEveryHeader(string path)
	{
		await using var app = await BuildAndStartAsync();
		using var response = await GetAsync(app, path);

		await Assert.That(Header(response, "Content-Security-Policy")).IsEqualTo("frame-ancestors 'self'; object-src 'none'; base-uri 'self'");
		await Assert.That(Header(response, "X-Frame-Options")).IsEqualTo("SAMEORIGIN");
		await Assert.That(Header(response, "X-Content-Type-Options")).IsEqualTo("nosniff");
		await Assert.That(Header(response, "Referrer-Policy")).IsEqualTo("strict-origin-when-cross-origin");
	}

	[Test]
	public async Task HeaderTheEndpointSet_IsKept()
	{
		await using var app = await BuildAndStartAsync();
		using var response = await GetAsync(app, "/credentials");

		await Assert.That(Header(response, "Referrer-Policy")).IsEqualTo("no-referrer");
		await Assert.That(Header(response, "X-Frame-Options")).IsEqualTo("SAMEORIGIN");
	}

	[Test]
	public async Task SoftcodeHttpResponses_AreLeftToTheSoftcode()
	{
		await using var app = await BuildAndStartAsync();
		using var response = await GetAsync(app, "/http/page");

		await Assert.That(Header(response, "X-Frame-Options")).IsEqualTo("DENY");
		await Assert.That(Header(response, "Content-Security-Policy")).IsNull();
		await Assert.That(Header(response, "Referrer-Policy")).IsNull();
	}
}
