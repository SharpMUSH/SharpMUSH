using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class SqlProviderSelectionTests
{
	private static readonly ISecretStore Secrets = FixedSecrets.With(SecretDefinition.SqlPassword.Name, "test");

	private static IOptionsMonitor<SharpMUSHOptions> CreateTestConfig(string? sqlHost, string? sqlDatabase, string? sqlUsername, string sqlPlatform)
	{
		var options = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();

		var baseConfig = ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst");

		var config = baseConfig with
		{
			Net = baseConfig.Net with
			{
				SqlHost = sqlHost,
				SqlDatabase = sqlDatabase,
				SqlUsername = sqlUsername,
				SqlPlatform = sqlPlatform
			}
		};

		options.CurrentValue.Returns(config);
		return options;
	}

	[Test]
	public async Task Test_MySqlProvider_Selection()
	{
		var optionsMonitor = CreateTestConfig("localhost", "test", "test", "mysql");
		var service = new SqlService(optionsMonitor, Secrets);

		await Assert.That(service.IsAvailable).IsTrue();
	}

	/// <summary>A stored password this host cannot decrypt is not a missing one: SQL stays off rather than connecting without it.</summary>
	[Test]
	public async Task An_unreadable_password_leaves_sql_unavailable()
	{
		var secrets = Substitute.For<ISecretStore>();
		secrets.GetAsync(SecretDefinition.SqlPassword.Name, Arg.Any<CancellationToken>())
			.Returns(new FoundResult<string>(new Error<string>("cannot be decrypted")));
		var service = new SqlService(CreateTestConfig("localhost", "test", "test", "mysql"), secrets);

		await Assert.That(service.IsAvailable).IsFalse();
	}

	[Test]
	public async Task Test_MariaDbProvider_Selection()
	{
		var optionsMonitor = CreateTestConfig("localhost", "test", "test", "mariadb");
		var service = new SqlService(optionsMonitor, Secrets);

		await Assert.That(service.IsAvailable).IsTrue();
	}

	[Test]
	public async Task Test_PostgreSqlProvider_Selection()
	{
		var optionsMonitor = CreateTestConfig("localhost", "test", "test", "postgresql");
		var service = new SqlService(optionsMonitor, Secrets);

		await Assert.That(service.IsAvailable).IsTrue();
	}

	[Test]
	public async Task Test_PostgresProvider_Selection()
	{
		var optionsMonitor = CreateTestConfig("localhost", "test", "test", "postgres");
		var service = new SqlService(optionsMonitor, Secrets);

		await Assert.That(service.IsAvailable).IsTrue();
	}

	[Test]
	public async Task Test_SqliteProvider_Selection()
	{
		var optionsMonitor = CreateTestConfig("", "test.db", "", "sqlite");
		var service = new SqlService(optionsMonitor, Secrets);

		await Assert.That(service.IsAvailable).IsTrue();
	}

	[Test]
	public async Task Test_UnsupportedProvider_ThrowsException()
	{
		var optionsMonitor = CreateTestConfig("localhost", "test", "test", "unsupported");

		await Assert.That(() => new SqlService(optionsMonitor, Secrets).IsAvailable)
			.Throws<NotSupportedException>()
			.WithMessage("SQL platform 'unsupported' is not supported. Supported platforms: mysql, postgresql, sqlite");
	}

	[Test]
	public async Task Test_MySqlProvider_Escape()
	{
		var optionsMonitor = CreateTestConfig("localhost", "test", "test", "mysql");
		var service = new SqlService(optionsMonitor, Secrets);

		var escaped = service.Escape("test'string");

		await Assert.That(escaped).IsEqualTo("test\\'string");
	}

	[Test]
	public async Task Test_PostgreSqlProvider_Escape()
	{
		var optionsMonitor = CreateTestConfig("localhost", "test", "test", "postgresql");
		var service = new SqlService(optionsMonitor, Secrets);

		var escaped = service.Escape("test'string");

		await Assert.That(escaped).IsEqualTo("test''string");
	}

	[Test]
	public async Task Test_SqliteProvider_Escape()
	{
		var optionsMonitor = CreateTestConfig("", "test.db", "", "sqlite");
		var service = new SqlService(optionsMonitor, Secrets);

		var escaped = service.Escape("test'string");

		await Assert.That(escaped).IsEqualTo("test''string");
	}
}
