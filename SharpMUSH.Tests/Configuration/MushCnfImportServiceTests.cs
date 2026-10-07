using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Server;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// A database import applies the mush.cnf that came with it, and takes it back when the conversion does not
/// finish: the options and the object references the file named are as they were before.
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

	[Test]
	public async Task Restoring_puts_back_the_options_and_references_the_import_replaced()
	{
		var before = TestSharpMushOptions.Create();
		var store = new MemoryStore();
		var service = new MushCnfImportService(
			StoreConfigOptionWriter.Create(store, new TestSharpMushOptions.FixedWrapper(before), new ConfigurationReloadService()),
			store, NullLogger<MushCnfImportService>.Instance);

		var snapshot = await service.SnapshotAsync();
		var imported = await service.ApplyAsync(await service.ReadAsync("mud_name Elsewhere\nevent_handler 42\n"));

		await Assert.That(imported.Net.MudName).IsEqualTo("Elsewhere");
		await Assert.That(((MushCnfObjectReferences)store.Server[nameof(MushCnfObjectReferences)])
			.Named(nameof(DatabaseOptions.EventHandler), 42)).IsTrue();

		await service.RestoreAsync(snapshot);

		await Assert.That(store.Server[nameof(SharpMUSHOptions)]).IsSameReferenceAs(before);
		await Assert.That(((MushCnfObjectReferences)store.Server[nameof(MushCnfObjectReferences)]).Values).IsEmpty();
	}
}
