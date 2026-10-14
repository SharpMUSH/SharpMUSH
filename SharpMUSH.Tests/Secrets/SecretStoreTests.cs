using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Secrets;

/// <summary>
/// The secret store over an in-memory world and a key ring in a directory of its own, so each test
/// owns both halves: what a backup carries (the world) and what stays on the host (the ring).
/// </summary>
public class SecretStoreTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 14, 22, 0, 0, TimeSpan.Zero);
	private static readonly SecretAuthor Grave = new("#1:1", "Grave");

	private static readonly SecretDefinition Token = new("discord.token", "The Discord bot's token.", "discord.admin");

	/// <summary>
	/// A world, a key ring beside it and a store over both. The ring caches its keys and refreshes them in the
	/// background, so a test that changes keys reads through <see cref="Restart"/>, as the server would after one.
	/// </summary>
	private sealed class Host : IDisposable
	{
		private readonly string _directory = Path.Combine(Path.GetTempPath(), $"secret-store-{Guid.NewGuid():N}");
		private readonly Dictionary<string, string>? _environment;
		private ServiceProvider _ring;

		public Host(InMemoryWorld? world = null, Dictionary<string, string>? environment = null)
		{
			Directory.CreateDirectory(Files);
			_environment = environment;
			World = world ?? new InMemoryWorld();
			_ring = Ring(Path.Combine(_directory, "keys"));
			Store = Open();
		}

		public string Files => Path.Combine(_directory, "run-secrets");
		public InMemoryWorld World { get; }
		public SecretStore Store { get; private set; }
		public IKeyManager Keys => _ring.GetRequiredService<IKeyManager>();

		public void Restart()
		{
			_ring.Dispose();
			_ring = Ring(Path.Combine(_directory, "keys"));
			Store = Open();
		}

		private SecretStore Open() => new(World, _ring.GetRequiredService<IDataProtectionProvider>(),
			[SecretDefinition.SqlPassword, Token],
			new SecretStoreOptions(Files, name => _environment?.GetValueOrDefault(name)),
			new FixedClock(Now), NullLogger<SecretStore>.Instance);

		public void Dispose()
		{
			_ring.Dispose();
			Directory.Delete(_directory, true);
		}
	}

	private static ServiceProvider Ring(string directory)
	{
		var services = new ServiceCollection();
		services.AddDataProtection().SetApplicationName("SharpMUSH").PersistKeysToFileSystem(new DirectoryInfo(directory));
		return services.BuildServiceProvider();
	}

	[Test]
	public async Task AStoredSecretReadsBackButTheWorldHoldsOnlyCiphertext()
	{
		using var host = new Host();

		var status = (await host.Store.SetAsync("sql.password", "hunter2-and-more", Grave)).Expect<SecretStatus>();

		await Assert.That((await host.Store.GetAsync("sql.password")).Expect<string>()).IsEqualTo("hunter2-and-more");
		await Assert.That(status).IsEqualTo(new SecretStatus("sql.password", SecretDefinition.SqlPassword.Description,
			SecretSource.Stored, null, true, Now, Grave, null, true));
		await Assert.That(host.World.Json).DoesNotContain("hunter2");
	}

	[Test]
	public async Task AWorldCopiedToAHostWithoutItsKeyRingOpensNothing()
	{
		using var original = new Host();
		await original.Store.SetAsync("sql.password", "hunter2", Grave);

		using var thief = new Host(original.World.Copy());

		await Assert.That((await thief.Store.GetAsync("sql.password")).Value).IsTypeOf<Error<string>>();
		await Assert.That((await thief.Store.ListAsync()).Single(s => s.Name == "sql.password").Source).IsEqualTo(SecretSource.Unreadable);
	}

	[Test]
	public async Task OneSecretsCiphertextCopiedOverAnothersDoesNotDecrypt()
	{
		using var host = new Host();
		await host.Store.SetAsync("sql.password", "hunter2", Grave);
		host.World.Edit(data => data.Secrets["discord.token"] = data.Secrets["sql.password"]);

		await Assert.That((await host.Store.GetAsync("discord.token")).Value).IsTypeOf<Error<string>>();
	}

	[Test]
	public async Task AValueWrittenWithARetiredKeyIsWrittenAgainWithTheCurrentOne()
	{
		using var host = new Host();
		await host.Store.SetAsync("sql.password", "hunter2", Grave);
		var before = host.World.Read().Secrets["sql.password"];

		host.Keys.CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90));
		host.Restart();

		await Assert.That((await host.Store.GetAsync("sql.password")).Expect<string>()).IsEqualTo("hunter2");
		var after = host.World.Read().Secrets["sql.password"];
		await Assert.That(after.Ciphertext).IsNotEqualTo(before.Ciphertext);
		await Assert.That(after with { Ciphertext = before.Ciphertext }).IsEqualTo(before)
			.Because("rewriting under a new key keeps who stored it and when");
		await Assert.That((await host.Store.GetAsync("sql.password")).Expect<string>()).IsEqualTo("hunter2");
	}

	[Test]
	public async Task AValueWrittenWithARevokedKeyIsUnreadable()
	{
		using var host = new Host();
		await host.Store.SetAsync("sql.password", "hunter2", Grave);

		host.Keys.RevokeAllKeys(DateTimeOffset.UtcNow, "compromised");
		host.Restart();

		await Assert.That((await host.Store.GetAsync("sql.password")).Value).IsTypeOf<Error<string>>();
	}

	[Test]
	public async Task AnEnvironmentVariableOverridesAFileWhichOverridesTheStoredValue()
	{
		var environment = new Dictionary<string, string>();
		using var host = new Host(environment: environment);
		await host.Store.SetAsync("sql.password", "stored", Grave);

		await File.WriteAllTextAsync(Path.Combine(host.Files, "sql.password"), "from-file\n");
		await Assert.That((await host.Store.GetAsync("sql.password")).Expect<string>()).IsEqualTo("from-file");
		await Assert.That((await host.Store.ListAsync()).Single(s => s.Name == "sql.password"))
			.IsEqualTo(new SecretStatus("sql.password", SecretDefinition.SqlPassword.Description, SecretSource.File,
				Path.Combine(host.Files, "sql.password"), true, Now, Grave, null, true));

		environment["SHARPMUSH_SECRET_SQL_PASSWORD"] = "from-environment";
		await Assert.That((await host.Store.GetAsync("sql.password")).Expect<string>()).IsEqualTo("from-environment");
		await Assert.That((await host.Store.ListAsync()).Single(s => s.Name == "sql.password").Override)
			.IsEqualTo("SHARPMUSH_SECRET_SQL_PASSWORD");
	}

	[Test]
	public async Task ClearingLeavesAnOverrideAndSaysWhenNothingWasStored()
	{
		using var host = new Host(environment: new() { ["SHARPMUSH_SECRET_DISCORD_TOKEN"] = "from-environment" });
		await host.Store.SetAsync("discord.token", "stored", Grave);

		var cleared = (await host.Store.ClearAsync("discord.token")).Expect<SecretStatus>();

		await Assert.That(cleared.Source).IsEqualTo(SecretSource.Environment);
		await Assert.That(cleared.Stored).IsFalse();
		await Assert.That((await host.Store.GetAsync("discord.token")).Expect<string>()).IsEqualTo("from-environment");
		await Assert.That((await host.Store.ClearAsync("discord.token")).Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task TheListHoldsEveryDeclaredSecretAndEveryStoredOne()
	{
		using var host = new Host();
		await host.Store.SetAsync("webhook.mailrelay.signing", "s3cret", Grave);

		var list = await host.Store.ListAsync();

		await Assert.That(list.Select(s => s.Name)).IsEquivalentTo(new[] { "discord.token", "sql.password", "webhook.mailrelay.signing" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(list.Single(s => s.Name == "discord.token")).IsEqualTo(new SecretStatus("discord.token",
			Token.Description, SecretSource.Unset, null, false, null, null, "discord.admin", true));
		await Assert.That(list.Single(s => s.Name == "webhook.mailrelay.signing").Declared).IsFalse();
		await Assert.That((await host.Store.GetAsync("discord.token")).Value).IsTypeOf<NotFound>();
	}

	[Test]
	[Arguments("SQL.password")]
	[Arguments("../etc/passwd")]
	[Arguments(".hidden")]
	[Arguments("sql..password")]
	[Arguments("discord_token")]
	[Arguments("discord-token")]
	[Arguments("")]
	public async Task ANameThatIsNotOneIsRefused(string name)
	{
		using var host = new Host();

		await Assert.That((await host.Store.SetAsync(name, "value", Grave)).Value).IsTypeOf<Error<string>>();
		await Assert.That(host.World.Json).IsEqualTo("null");
	}

	[Test]
	public async Task AnEmptyOrOversizedValueIsRefused()
	{
		using var host = new Host();

		await Assert.That((await host.Store.SetAsync("sql.password", "", Grave)).Value).IsTypeOf<Error<string>>();
		await Assert.That((await host.Store.SetAsync("sql.password", new string('x', SecretStore.MaxValueLength + 1), Grave)).Value)
			.IsTypeOf<Error<string>>();
		await Assert.That((await host.Store.SetAsync("sql.password", new string('x', SecretStore.MaxValueLength), Grave)).Value)
			.IsTypeOf<SecretStatus>();
	}

	[Test]
	[Arguments("sql.password", "SHARPMUSH_SECRET_SQL_PASSWORD")]
	[Arguments("webhook.mailrelay.signing", "SHARPMUSH_SECRET_WEBHOOK_MAILRELAY_SIGNING")]
	public async Task AnEnvironmentVariableIsTheNameInUpperCaseWithUnderscores(string name, string variable)
		=> await Assert.That(SecretNames.EnvironmentVariable(name)).IsEqualTo(variable);

	public sealed class FixedClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}

	/// <summary>The world's server data, round-tripped through JSON as the database stores it.</summary>
	public sealed class InMemoryWorld : IExpandedObjectDataService
	{
		public string Json { get; private set; } = "null";

		public SecretsData Read() => JsonSerializer.Deserialize<SecretsData>(Json) ?? new SecretsData();

		public void Edit(Action<SecretsData> edit)
		{
			var data = Read();
			edit(data);
			Json = JsonSerializer.Serialize(data);
		}

		public InMemoryWorld Copy() => new() { Json = Json };

		public ValueTask<T?> GetExpandedDataAsync<T>(SharpObject obj) where T : class => throw new NotSupportedException();

		public ValueTask SetExpandedDataAsync<T>(T data, SharpObject obj, bool ignoreNull = false) where T : class
			=> throw new NotSupportedException();

		public ValueTask<T?> GetExpandedServerDataAsync<T>() where T : class
			=> ValueTask.FromResult(typeof(T) == typeof(SecretsData) ? JsonSerializer.Deserialize<T>(Json) : null);

		public ValueTask SetExpandedServerDataAsync<T>(T data, bool ignoreNull = false) where T : class
		{
			Json = JsonSerializer.Serialize(data);
			return ValueTask.CompletedTask;
		}
	}
}
