using Core.Arango;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Server;
using SharpMUSH.Server.ProtocolHandlers;

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