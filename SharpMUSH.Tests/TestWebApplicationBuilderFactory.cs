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

namespace SharpMUSH.Tests;

public class TestWebApplicationBuilderFactory<TProgram>(
	string sqlConnectionString,
	string configFile,
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
	}
}