using Core.Arango;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server;
using SharpMUSH.Server.ProtocolHandlers;

namespace SharpMUSH.Tests;

public class TestWebServer: Microsoft.AspNetCore.TestHost.TestServer
{
	public TestWebServer(ArangoConfiguration acnf, string configFile, INotifyService notifier) : base(WebHostBuilder(acnf, configFile, notifier))
	{
		var log = new LoggerConfiguration()
			.Enrich.FromLogContext()
			.WriteTo.Console(theme: AnsiConsoleTheme.Code)
			.MinimumLevel.Debug()
			.CreateLogger();
		
		Log.Logger = log;
	}

	public static IWebHostBuilder WebHostBuilder(ArangoConfiguration acnf, string configFile, INotifyService notifier)
	{
		var builder = WebApplication.CreateBuilder();
		var startup = new Startup(acnf, configFile, notifier);
		startup.ConfigureServices(builder.Services);

		builder.WebHost
			.UseKestrel(options => options.ListenLocalhost(4202, lo => lo.UseConnectionHandler<TelnetServer>()));
		
		return builder.WebHost;
	}
}