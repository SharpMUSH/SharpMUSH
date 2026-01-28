using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class SqlProviderSelectionTests
{
	[Test]
	public async Task Test_MySqlProvider_Selection()
	{
		var connectionString = "Server=localhost;Uid=test;Pwd=test;Database=test";
		var service = new SqlService(connectionString, "mysql");
		
		await Assert.That(service.IsAvailable).IsTrue();
	}

	[Test]
	public async Task Test_MariaDbProvider_Selection()
	{
		var connectionString = "Server=localhost;Uid=test;Pwd=test;Database=test";
		var service = new SqlService(connectionString, "mariadb");
		
		await Assert.That(service.IsAvailable).IsTrue();
	}

	[Test]
	public async Task Test_PostgreSqlProvider_Selection()
	{
		var connectionString = "Host=localhost;Username=test;Password=test;Database=test";
		var service = new SqlService(connectionString, "postgresql");
		
		await Assert.That(service.IsAvailable).IsTrue();
	}

	[Test]
	public async Task Test_PostgresProvider_Selection()
	{
		var connectionString = "Host=localhost;Username=test;Password=test;Database=test";
		var service = new SqlService(connectionString, "postgres");
		
		await Assert.That(service.IsAvailable).IsTrue();
	}

	[Test]
	public async Task Test_SqliteProvider_Selection()
	{
		var connectionString = "Data Source=test.db";
		var service = new SqlService(connectionString, "sqlite");
		
		await Assert.That(service.IsAvailable).IsTrue();
	}

	[Test]
	public async Task Test_UnsupportedProvider_ThrowsException()
	{
		var connectionString = "test";
		
		await Assert.That(() => new SqlService(connectionString, "unsupported"))
			.Throws<NotSupportedException>()
			.WithMessage("SQL platform 'unsupported' is not supported. Supported platforms: mysql, postgresql, sqlite");
	}

	[Test]
	public async Task Test_MySqlProvider_Escape()
	{
		var connectionString = "Server=localhost;Uid=test;Pwd=test;Database=test";
		var service = new SqlService(connectionString, "mysql");
		
		var escaped = service.Escape("test'string");
		
		await Assert.That(escaped).IsEqualTo("test\\'string");
	}

	[Test]
	public async Task Test_PostgreSqlProvider_Escape()
	{
		var connectionString = "Host=localhost;Username=test;Password=test;Database=test";
		var service = new SqlService(connectionString, "postgresql");
		
		var escaped = service.Escape("test'string");
		
		await Assert.That(escaped).IsEqualTo("test''string");
	}

	[Test]
	public async Task Test_SqliteProvider_Escape()
	{
		var connectionString = "Data Source=test.db";
		var service = new SqlService(connectionString, "sqlite");
		
		var escaped = service.Escape("test'string");
		
		await Assert.That(escaped).IsEqualTo("test''string");
	}
}
