using Core.Arango;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server;
using SharpMUSH.Server.Strategy.Redis;

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
			.MinimumLevel.Verbose()
			.CreateLogger();

		Log.Logger = log;

		// Initialize Redis strategy for benchmarks
		var redisStrategy = RedisStrategyProvider.GetStrategy();
		redisStrategy.InitializeAsync().AsTask().Wait();

		var startup = new Startup(acnf, colorFile, redisStrategy);

		var substitute = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		substitute.CurrentValue.Returns(ReadPennMushConfig.Create(configFile));

		builder.ConfigureServices(services =>
		{
			// Build a minimal configuration for Serilog to read from
			var configuration = new ConfigurationBuilder()
				.SetBasePath(AppContext.BaseDirectory)
				.AddJsonFile("appsettings.json", optional: true)
				.Build();
			startup.ConfigureServices(services, configuration);
		});
		builder.ConfigureTestServices(sc =>
			{
				sc.RemoveAll<IOptionsWrapper<SharpMUSHOptions>>();
				sc.AddSingleton(x => substitute);
			}
		);
	}
}