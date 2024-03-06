using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Hosting;
using SharpMUSH.Server;
using SharpMUSH.Server.ProtocolHandlers;
using Serilog.Sinks.SystemConsole.Themes;
using Serilog;

namespace SharpMUSH.IntegrationTests
{
	public class Infrastructure : TestServer
	{
		public Infrastructure() : base(WebHostBuilder()) {
			var log = new LoggerConfiguration()
				.Enrich.FromLogContext()
				.WriteTo.Console(theme: AnsiConsoleTheme.Code)
				.MinimumLevel.Debug()
				.CreateLogger();

			Log.Logger = log;
		}

		public static IWebHostBuilder WebHostBuilder() =>
			WebHost.CreateDefaultBuilder()
				.UseStartup<Startup>()
				.UseEnvironment("test")
				.UseKestrel(options => options.ListenLocalhost(4202, builder => builder.UseConnectionHandler<TelnetServer>()));

	}
}
