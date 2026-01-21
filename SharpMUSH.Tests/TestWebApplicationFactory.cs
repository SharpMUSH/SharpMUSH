using Core.Arango;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.ClassDataSources;
using TUnit.AspNetCore;
using TUnit.AspNetCore.Extensions;

namespace SharpMUSH.Tests;

public class TestWebApplicationFactory : TestWebApplicationFactory<SharpMUSH.Server.Program>
{
	[ClassDataSource<ArangoDbTestServer>(Shared = SharedType.PerTestSession)]
	public ArangoDbTestServer ArangoDbTestServer { get; init; } = null!;
	
	[ClassDataSource<RedPandaTestServer>(Shared = SharedType.PerTestSession)]
	public RedPandaTestServer RedPandaTestServer { get; init; } = null!;
	
	[ClassDataSource<MySqlTestServer>(Shared = SharedType.PerTestSession)]
	public MySqlTestServer MySqlTestServer { get; init; } = null!;
	
	[ClassDataSource<PrometheusTestServer>(Shared = SharedType.PerTestSession)]
	public PrometheusTestServer PrometheusTestServer { get; init; } = null!;

	[ClassDataSource<RedisTestServer>(Shared = SharedType.PerTestSession)]
	public RedisTestServer RedisTestServer { get; init; } = null!;
	
	[ClassDataSource<TestLoggerFactoryDataSource>(Shared = SharedType.PerTestSession)]
	public TestLoggerFactoryDataSource TestLoggerFactory { get; init; } = null!;
	
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		var configFile = Path.Join(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");
		var colorFile = Path.Combine(AppContext.BaseDirectory, "colors.json");
		if (!File.Exists(colorFile))
		{
			var tempColorFile = Path.Combine(Path.GetTempPath(), "colors.json");
			File.WriteAllText(tempColorFile, "{}");
			try
			{
				Directory.CreateDirectory(AppContext.BaseDirectory);
				File.Copy(tempColorFile, colorFile, true);
			}
			catch
			{
				// If we can't create it in the base directory, that's OK
				// The startup will handle the missing file
			}
		}

		builder.ConfigureTestServices(sc =>
		{
			var substitute = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
			substitute.CurrentValue.Returns(ReadPennMushConfig.Create(configFile));

			sc.ReplaceService(substitute);
			// Database name is now determined in Program.cs based on ARANGO_TEST_CONNECTION_STRING env var
			// Use wrapper that delegates to per-test NotifyService instances
			sc.ReplaceService<INotifyService>(new TestNotifyServiceWrapper());
			sc.ReplaceService(new SqlService(MySqlTestServer.Instance.GetConnectionString()));
			
			// Replace LoggerFactory with test-session-scoped instance to prevent ObjectDisposedException
			// when services like Quartz try to create loggers after WebApplication disposal
			sc.ReplaceService<ILoggerFactory>(TestLoggerFactory.Instance);
		});
	}
}