using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.ConnectionServer.Services;
using Microsoft.AspNetCore.TestHost;
using Serilog;
using TUnit.AspNetCore;

namespace SharpMUSH.Tests;

/// <summary>
/// Test factory for SharpMUSH.ConnectionServer that configures the test environment.
/// </summary>
public class ConnectionServerTestWebApplicationBuilderFactory<TProgram>(
	string natsUrl) :
	TestWebApplicationFactory<TProgram> where TProgram : class
{
	/// <summary>
	/// Lifecycle Step 4: Runs BEFORE Program.cs startup.
	/// ConfigureWebHost is called BEFORE the application's ConfigureServices runs.
	/// This allows us to set up environment variables and override services before
	/// the Program.cs Configure method executes.
	/// </summary>
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		Log.Logger = TestDiagnostics.CreateLogger();
		TestDiagnostics.ConfigureHost(builder);

		Environment.SetEnvironmentVariable("NATS_URL", natsUrl);

		builder.ConfigureTestServices(sc =>
		{
			// This TestServer fixture isolates in-process gateway behavior. TCP/Pueblo/MXP integration
			// uses TelnetIntegrationFixture, which starts the real Unix-socket rendering worker.
			sc.AddSingleton<IMarkupOutputRenderer, MarkupOutputRenderer>();
			sc.AddSingleton<IOutputTransformService, OutputTransformService>();
		});
	}
}
