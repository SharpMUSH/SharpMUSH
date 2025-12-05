using Core.Arango;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server;
using SharpMUSH.Server.Strategy.Prometheus;

namespace SharpMUSH.Benchmarks;

public class TestWebApplicationBuilderFactory<TProgram>(
		ArangoConfiguration acnf,
		string configFile,
		string colorFile) :
	WebApplicationFactory<TProgram> where TProgram : class
{
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		var log = new LoggerConfiguration()
			.Enrich.FromLogContext()
			.WriteTo.Console(theme: AnsiConsoleTheme.Code)
			.MinimumLevel.Debug()
			.CreateLogger();

		Log.Logger = log;

		// Initialize Prometheus strategy for benchmarks
		var prometheusStrategy = PrometheusStrategyProvider.GetStrategy();
		prometheusStrategy.InitializeAsync().AsTask().Wait();

		var startup = new Startup(acnf, colorFile, prometheusStrategy);

		var substitute = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		substitute.CurrentValue.Returns(ReadPennMushConfig.Create(configFile));

		builder.ConfigureServices(startup.ConfigureServices);
		builder.ConfigureTestServices(sc =>
			{
				sc.RemoveAll<IOptionsWrapper<SharpMUSHOptions>>();
				sc.AddSingleton(x => substitute);
			}
		);
	}
}