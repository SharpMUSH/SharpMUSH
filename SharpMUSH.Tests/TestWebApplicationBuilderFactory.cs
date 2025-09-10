using Core.Arango;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server;
using SharpMUSH.Server.ProtocolHandlers;

namespace SharpMUSH.Tests;

public class TestWebApplicationBuilderFactory<TProgram>(
		ArangoConfiguration acnf, 
		string configFile, 
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
		
		var startup = new Startup(acnf, configFile, notifier);

		builder.ConfigureServices(x => startup.ConfigureServices(x));

		builder.UseKestrel(options 
			=> options.ListenLocalhost(4202, lo => lo.UseConnectionHandler<TelnetServer>()));
	}
}