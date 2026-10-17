using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Server;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Authentication.External;

public class ExternalEndpointRateLimitTests
{
	[Test]
	[Arguments(typeof(ExternalLoginController))]
	[Arguments(typeof(AccountSecurityController))]
	[Arguments(typeof(NativeCharacterCredentialsController))]
	public async Task ActualWebApiRegistrationAdmitsThenThrottlesEachCredentialControllersMetadata(Type controller)
	{
		var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
		builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
		{
			["RateLimiting:PublicApi:PermitLimit"] = "1",
			["RateLimiting:PublicApi:WindowSeconds"] = "60",
			["RateLimiting:PublicApi:QueueLimit"] = "0"
		});
		// Exercise the actual internal host registration without widening production visibility or
		// duplicating its policy list. Only the endpoint body is isolated from controller dependencies.
		var registration = typeof(Startup).Assembly.GetType("SharpMUSH.Server.Registration.WebHostRegistration")
			?.GetMethod("AddSharpMushWebApi", BindingFlags.Static | BindingFlags.Public)
			?? throw new InvalidOperationException("Missing production web API registration.");
		registration.Invoke(null, [builder.Services, builder.Configuration]);
		var metadata = controller.GetCustomAttribute<EnableRateLimitingAttribute>()
			?? throw new InvalidOperationException("Missing credential rate-limit metadata.");
		await using var app = builder.Build();
		app.UseRouting(); app.UseRateLimiter();
		app.MapGet("/credential-surface", () => "ok").WithMetadata(metadata);
		await app.StartAsync(); using var client = app.GetTestClient();
		using var first = await client.GetAsync("/credential-surface");
		await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
		using var second = await client.GetAsync("/credential-surface");
		await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
	}
}
