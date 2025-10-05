using Core.Arango;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server;
using SharpMUSH.Server.ProtocolHandlers;

namespace SharpMUSH.Benchmarks;

public class TestWebApplicationBuilderFactory<TProgram>(
		ArangoConfiguration acnf, 
		string configFile, 
		string colorFile,
		INotifyService? notifier): 
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
		
		var startup = new Startup(acnf, colorFile, notifier);

		builder.ConfigureServices(x => startup.ConfigureServices(x));
		builder.ConfigureTestServices(sc => 
			sc.AddOptions<SharpMUSHOptions>()
				.Configure(_ => ReadPennMushConfig.Create(configFile))
		);

		builder.UseKestrel(options 
			=> options.ListenLocalhost(4203, lo => lo.UseConnectionHandler<TelnetServer>()));
	}
}