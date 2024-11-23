using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Connections;
using SharpMUSH.Server;
using SharpMUSH.Server.ProtocolHandlers;
using Serilog.Sinks.SystemConsole.Themes;
using Serilog;
using Core.Arango;

namespace SharpMUSH.Tests;

public class Infrastructure : TestServer
{
		public Infrastructure(ArangoConfiguration acnf) : base(WebHostBuilder(acnf))
		{
				var log = new LoggerConfiguration()
					.Enrich.FromLogContext()
					.WriteTo.Console(theme: AnsiConsoleTheme.Code)
					.MinimumLevel.Debug()
					.CreateLogger();

				Log.Logger = log;
		}

		public static IWebHostBuilder WebHostBuilder(ArangoConfiguration acnf) =>
			WebHost.CreateDefaultBuilder()
				.UseStartup(x => new Startup(acnf))
				.UseEnvironment("test")
				.UseKestrel(options => options.ListenLocalhost(4202, builder => builder.UseConnectionHandler<TelnetServer>()));
}