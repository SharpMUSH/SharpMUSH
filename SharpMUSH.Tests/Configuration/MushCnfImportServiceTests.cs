using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Secrets;
using SharpMUSH.Tests.Server;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// A database import applies the mush.cnf that came with it, and takes it back when the conversion does not
/// finish: the options, the object references the file named and the secrets it set are as they were before. A
/// secret the file names goes into the secret store, never into the options.
/// </summary>
public class MushCnfImportServiceTests
{
	/// <summary>Keeps the last value written under each name, as the store would.</summary>
	private sealed class MemoryStore : IExpandedDataStore
	{
		public Dictionary<string, object> Server { get; } = [];

		public ValueTask SetExpandedObjectData(string sharpObjectId, string dataType, dynamic data,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<T?> GetExpandedObjectData<T>(string sharpObjectId, string dataType,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask SetExpandedServerData(string dataType, dynamic data, CancellationToken cancellationToken = default)
		{
			Server[dataType] = data;
			return ValueTask.CompletedTask;
		}

		public ValueTask<T?> GetExpandedServerData<T>(string dataType, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(Server.TryGetValue(dataType, out var data) ? (T?)data : default);
	}

	private static readonly SecretAuthor Importer = new("account-1", "admin");

	private static SecretStore Secrets() => new(new SecretStoreTests.InMemoryWorld(), new EphemeralDataProtectionProvider(),
		[SecretDefinition.SqlPassword], new SecretStoreOptions(Path.Join(Path.GetTempPath(), $"no-secrets-{Guid.NewGuid():N}"), _ => null),
		TimeProvider.System, NullLogger<SecretStore>.Instance);

	private static MushCnfImportService Service(MemoryStore store, SecretStore secrets, SharpMUSHOptions? before = null)
		=> new(StoreConfigOptionWriter.Create(store, new TestSharpMushOptions.FixedWrapper(before ?? TestSharpMushOptions.Create()),
			new ConfigurationReloadService()), store, secrets, NullLogger<MushCnfImportService>.Instance);

	[Test]
	public async Task Restoring_puts_back_the_options_and_references_the_import_replaced()
	{
		var before = TestSharpMushOptions.Create();
		var store = new MemoryStore();
		var service = Service(store, Secrets(), before);

		var snapshot = await service.SnapshotAsync();
		var imported = await service.ApplyAsync(await service.ReadAsync("mud_name Elsewhere\nevent_handler 42\n"), Importer);

		await Assert.That(imported.Net.MudName).IsEqualTo("Elsewhere");
		await Assert.That(((MushCnfObjectReferences)store.Server[nameof(MushCnfObjectReferences)])
			.Named(nameof(DatabaseOptions.EventHandler), 42)).IsTrue();

		await service.RestoreAsync(snapshot);

		await Assert.That(store.Server[nameof(SharpMUSHOptions)]).IsSameReferenceAs(before);
		await Assert.That(((MushCnfObjectReferences)store.Server[nameof(MushCnfObjectReferences)]).Values).IsEmpty();
	}

	[Test]
	public async Task The_sql_password_goes_into_the_secret_store()
	{
		var secrets = Secrets();
		var service = Service(new MemoryStore(), secrets);

		var import = await service.ReadAsync("sql_username mush\nsql_password hunter2 \n");
		await service.ApplyAsync(import, Importer);

		await Assert.That(PennMushConfigImport.SecretDirectives["sql_password"]).IsEqualTo(SecretDefinition.SqlPassword.Name);
		await Assert.That((await secrets.GetAsync(SecretDefinition.SqlPassword.Name)).Expect<string>()).IsEqualTo("hunter2");
		await Assert.That((await secrets.ListAsync()).Single(s => s.Name == SecretDefinition.SqlPassword.Name).SetBy)
			.IsEqualTo(Importer);
		await Assert.That(import.Skipped).DoesNotContain(line => line.Contains("hunter2"));
	}

	[Test]
	public async Task Restoring_puts_back_the_secrets_the_import_set()
	{
		var secrets = Secrets();
		var service = Service(new MemoryStore(), secrets);

		var empty = await service.SnapshotAsync();
		await service.ApplyAsync(await service.ReadAsync("sql_password first\n"), Importer);
		var first = await service.SnapshotAsync();
		await service.ApplyAsync(await service.ReadAsync("sql_password second\n"), Importer);

		await service.RestoreAsync(first);
		await Assert.That((await secrets.GetAsync(SecretDefinition.SqlPassword.Name)).Expect<string>()).IsEqualTo("first");

		await service.RestoreAsync(empty);
		await Assert.That((await secrets.GetAsync(SecretDefinition.SqlPassword.Name)).Value).IsTypeOf<NotFound>();
	}
}
