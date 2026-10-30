using LightningException = LightningDB.LightningException;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

public class BundledPluginTableCapacityTests
{
	private readonly List<string> _paths = [];

	private string NewPath()
	{
		var path = Path.Combine(Path.GetTempPath(), "bundled-table-capacity-" + Guid.NewGuid().ToString("N"));
		_paths.Add(path);
		return path;
	}

	[After(Test)]
	public async Task Cleanup()
	{
		foreach (var path in _paths) await FixtureDirectoryCleanup.DeleteAsync(path);
	}

	[Test]
	public async Task DefaultEnvironmentOpensTheActualCoreAndBundledPluginStorageRegistrations()
	{
		// The Server project builds these shipped artifacts; the regular test host deliberately removes them.
		var output = new DirectoryInfo(AppContext.BaseDirectory);
		var repository = output.Parent!.Parent!.Parent!.Parent!.FullName;
		var builtIn = Path.Combine(repository, "SharpMUSH.Server", "bin", output.Parent.Name, output.Name, "plugins");
		await Assert.That(Directory.Exists(builtIn)).IsTrue().Because("the real shipped plugin output must be built with the Server dependency");
		var path = NewPath();
		await using var database = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = Path.Combine(path, "world"), MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		var storage = new RecordingAccessor(database);
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<ILightningStorageAccessor>(storage);
		var catalog = PluginCatalog.Build(services, new PluginDirectories(builtIn, Path.Combine(path, "installed")), NullLogger.Instance);
		await Assert.That(catalog.Plugins.Select(plugin => plugin.Id)).IsEquivalentTo(new[] { "discord", "lsp", "mcp", "scene", "world" });
		// Resolve real registrations, rather than copying table names or manufacturing a threshold-sized schema.
		var stores = catalog.Plugins.Select(plugin => plugin.GetType().Assembly).Distinct()
			.SelectMany(assembly => assembly.GetTypes())
			.Where(implementation => implementation is { IsClass: true, IsAbstract: false }
				&& implementation.GetConstructors().Any(constructor => constructor.GetParameters() is [{ ParameterType: var parameter }] && parameter == typeof(ILightningStorageAccessor)))
			.Select(implementation => new
			{
				Implementation = implementation,
				Registration = services.FirstOrDefault(descriptor => descriptor.ServiceType == implementation)
					?? services.First(descriptor => descriptor.ServiceType.IsAssignableFrom(implementation))
			}).ToArray();
		await Assert.That(stores.Select(store => store.Implementation.Assembly.GetName().Name ?? throw new InvalidOperationException("Unnamed plugin assembly.")).Distinct())
			.IsEquivalentTo(new[] { "SharpMUSH.Plugins.Discord", "SharpMUSH.Plugins.Scene", "SharpMUSH.Plugins.World" });
		using var provider = services.BuildServiceProvider();
		foreach (var store in stores)
			await Assert.That(provider.GetRequiredService(store.Registration.ServiceType).GetType()).IsEqualTo(store.Implementation);
		var allTables = Tables.All.Concat(storage.Opened).Distinct().ToArray();
		await Assert.That(database.Read(transaction => allTables.All(table => transaction.Count(table) == 0))).IsTrue();
		Console.WriteLine($"Bundled LMDB schema: core={Tables.All.Count}, plugin={storage.Opened.Except(Tables.All).Count()}, total={allTables.Length}, storage registrations={stores.Length}.");
	}

	[Test]
	public async Task ExplicitEnvironmentCapacityStillLimitsPluginTableOpening()
	{
		using var store = new LightningStore(new LightningStoreOptions { Path = NewPath(), MapSize = 256L << 20, MaxDatabases = Tables.All.Count });
		await Assert.That(() => store.OpenTable("plugin.explicit-capacity-probe", duplicates: false)).Throws<LightningException>();
	}

	private sealed class RecordingAccessor(ILightningStorageAccessor inner) : ILightningStorageAccessor
	{
		public HashSet<TableDef> Opened { get; } = [];
		public TableDef OpenTable(string name, bool duplicates)
		{
			var table = inner.OpenTable(name, duplicates);
			Opened.Add(table);
			return table;
		}
		public T Read<T>(Func<ITx, T> read) => inner.Read(read);
		public ValueTask<T> WriteAsync<T>(Func<ITx, T> job, CancellationToken ct = default) => inner.WriteAsync(job, ct);
		public IAsyncEnumerable<(byte[] Key, byte[] Value)> RangeAsync(TableDef table, byte[] prefix, int pageSize = 256, CancellationToken ct = default)
			=> inner.RangeAsync(table, prefix, pageSize, ct);
		public ValueTask CopyToAsync(string path, bool compact = true, CancellationToken ct = default) => inner.CopyToAsync(path, compact, ct);
	}
}
