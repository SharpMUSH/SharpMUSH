using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Server;

namespace SharpMUSH.Tests.Server.Controllers;

public class WikiCollectionCorsTests
{
	[Test]
	[Arguments("Development", false, true)]
	[Arguments("Production", true, true)]
	[Arguments("Production", false, false)]
	public async Task AllowedSeparatePortalOriginsCanReadExactWikiCounts(string environment, bool configuredOrigin, bool allowed)
	{
		var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
		builder.WebHost.UseTestServer();
		// This policy-only host does not construct unrelated engine services.
		builder.Host.UseDefaultServiceProvider(options => options.ValidateOnBuild = false);
		builder.Logging.ClearProviders();
		const string origin = "https://portal.example.test";
		if (configuredOrigin) builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = origin });
		var registration = typeof(Startup).Assembly.GetType("SharpMUSH.Server.Registration.WebHostRegistration")
			?.GetMethod("AddSharpMushHttpPipeline", BindingFlags.Static | BindingFlags.Public)
			?? throw new InvalidOperationException("Missing production HTTP registration.");
		registration.Invoke(null, [builder.Services, builder.Configuration, builder.Environment]);
		await using var app = builder.Build();
		app.UseRouting();
		app.UseCors();
		app.MapGet("/wiki-count", (HttpContext context) =>
		{
			context.Response.Headers["X-Total-Count"] = "4711";
			return Results.Ok(Array.Empty<object>());
		});
		await app.StartAsync();
		using var client = app.GetTestClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, "/wiki-count");
		request.Headers.Add("Origin", origin);
		using var response = await client.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsEqualTo(allowed);
		var exposed = response.Headers.TryGetValues("Access-Control-Expose-Headers", out var values)
			&& values.Any(value => value.Split(',').Any(header => header.Trim().Equals("X-Total-Count", StringComparison.OrdinalIgnoreCase)));
		await Assert.That(exposed).IsEqualTo(allowed)
			.Because("the browser must be able to read the collection count on permitted separate portal origins");
	}
}
