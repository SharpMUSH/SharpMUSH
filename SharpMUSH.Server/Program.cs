using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Connections;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Server.ProtocolHandlers;

namespace SharpMUSH.Server
{
	public class Program
	{
		static async Task Main(string[] args)
		{
			var log = new LoggerConfiguration()
				.Enrich.FromLogContext()
				.WriteTo.Console(theme: AnsiConsoleTheme.Code)
				.MinimumLevel.Debug()
				.CreateLogger();

			Log.Logger = log;

			CreateWebHostBuilder(args).Build().Run();
			await Task.CompletedTask;
		}

		public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
				WebHost.CreateDefaultBuilder(args)
					.UseStartup<Startup>()
					.UseKestrel(options => options.ListenLocalhost(4202, builder => builder.UseConnectionHandler<TelnetServer>()));
	}
}