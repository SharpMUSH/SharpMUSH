using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class SqlProviderSelectionTests
{
	private static IOptionsMonitor<SharpMUSHOptions> CreateTestConfig(string? sqlHost, string? sqlDatabase, string? sqlUsername, string? sqlPassword, string sqlPlatform)
	{
		var options = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		
		// Start with default config from test file
		var baseConfig = ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst");
		
		// Override with test SQL settings
		var config = baseConfig with
		{
			Net = baseConfig.Net with
			{
				SqlHost = sqlHost,
				SqlDatabase = sqlDatabase,
				SqlUsername = sqlUsername,
				SqlPassword = sqlPassword,
				SqlPlatform = sqlPlatform
			}
		};
		
		options.CurrentValue.Returns(config);
		return options;
	}

	[Test]
	public async Task Test_MySqlProvider_Selection()
	{
		var optionsMonitor = CreateTestConfig("localhost", "test", "test", "test", "mysql");
		var service = new SqlService(optionsMonitor);
		
		await Assert.That(service.IsAvailable).IsTrue();
	}

	[Test]
	public async Task Test_MariaDbProvider_Selection()
	{
		var optionsMonitor = CreateTestConfig("localhost", "test", "test", "test", "mariadb");
		var service = new SqlService(optionsMonitor);
		
		await Assert.That(service.IsAvailable).IsTrue();
	}

	[Test]
	public async Task Test_PostgreSqlProvider_Selection()
	{
		var optionsMonitor = CreateTestConfig("localhost", "test", "test", "test", "postgresql");
		var service = new SqlService(optionsMonitor);
		
		await Assert.That(service.IsAvailable).IsTrue();
	}

	[Test]
	public async Task Test_PostgresProvider_Selection()
	{
		var optionsMonitor = CreateTestConfig("localhost", "test", "test", "test", "postgres");
		var service = new SqlService(optionsMonitor);
		
		await Assert.That(service.IsAvailable).IsTrue();
	}

	[Test]
	public async Task Test_SqliteProvider_Selection()
	{
		var optionsMonitor = CreateTestConfig("", "test.db", "", "", "sqlite");
		var service = new SqlService(optionsMonitor);
		
		await Assert.That(service.IsAvailable).IsTrue();
	}

	[Test]
	public async Task Test_UnsupportedProvider_ThrowsException()
	{
		var optionsMonitor = CreateTestConfig("localhost", "test", "test", "test", "unsupported");
		
		await Assert.That(() => new SqlService(optionsMonitor).IsAvailable)
			.Throws<NotSupportedException>()
			.WithMessage("SQL platform 'unsupported' is not supported. Supported platforms: mysql, postgresql, sqlite");
	}

	[Test]
	public async Task Test_MySqlProvider_Escape()
	{
		var optionsMonitor = CreateTestConfig("localhost", "test", "test", "test", "mysql");
		var service = new SqlService(optionsMonitor);
		
		var escaped = service.Escape("test'string");
		
		await Assert.That(escaped).IsEqualTo("test\\'string");
	}

	[Test]
	public async Task Test_PostgreSqlProvider_Escape()
	{
		var optionsMonitor = CreateTestConfig("localhost", "test", "test", "test", "postgresql");
		var service = new SqlService(optionsMonitor);
		
		var escaped = service.Escape("test'string");
		
		await Assert.That(escaped).IsEqualTo("test''string");
	}

	[Test]
	public async Task Test_SqliteProvider_Escape()
	{
		var optionsMonitor = CreateTestConfig("", "test.db", "", "", "sqlite");
		var service = new SqlService(optionsMonitor);
		
		var escaped = service.Escape("test'string");
		
		await Assert.That(escaped).IsEqualTo("test''string");
	}
}
