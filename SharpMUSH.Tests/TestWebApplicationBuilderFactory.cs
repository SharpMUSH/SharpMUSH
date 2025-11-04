using Core.Arango;
using Microsoft.AspNetCore.Connections;
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
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server;
using SharpMUSH.Server.ProtocolHandlers;

namespace SharpMUSH.Tests;

public class TestWebApplicationBuilderFactory<TProgram>(
	ArangoConfiguration acnf,
	string sqlConnectionString,
	string configFile,
	string colorFile,
	INotifyService notifier) :
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

		var startup = new Startup(acnf, colorFile);

		builder.ConfigureServices(startup.ConfigureServices);
		builder.ConfigureTestServices(sc =>
			{
				var substitute = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
				substitute.CurrentValue.Returns(ReadPennMushConfig.Create(configFile));

				sc.RemoveAll<IOptionsWrapper<SharpMUSHOptions>>();
				sc.AddSingleton(substitute);

				sc.RemoveAll<INotifyService>();
				sc.AddSingleton(notifier);

				sc.RemoveAll<ISqlService>();
				sc.AddSingleton<ISqlService>(new SqlService(sqlConnectionString));
			}
		);

		builder.UseKestrel(options
			=> options.ListenLocalhost(4203, lo => lo.UseConnectionHandler<TelnetServer>()));
	}
}