using Core.Arango;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server;
using SharpMUSH.Server.ProtocolHandlers;

namespace SharpMUSH.Benchmarks;

public class Infrastructure : TestServer
{
	public Infrastructure(ArangoConfiguration acnf, string configFile, INotifyService? notifier) : base(WebHostBuilder(acnf, configFile, notifier))
	{
		var log = new LoggerConfiguration()
			.Enrich.FromLogContext()
			.WriteTo.Console(theme: AnsiConsoleTheme.Code)
			.MinimumLevel.Debug()
			.CreateLogger();
		
		Log.Logger = log;
	}

	private static IWebHostBuilder WebHostBuilder(ArangoConfiguration acnf, string configFile, INotifyService? notifier) =>
		WebHost.CreateDefaultBuilder()
			.UseStartup(_ => new Startup(acnf, configFile, notifier))
			.ConfigureServices(x => { })
			.UseEnvironment("test")
			.UseKestrel(options => options.ListenLocalhost(4202, builder => builder.UseConnectionHandler<TelnetServer>()));
}