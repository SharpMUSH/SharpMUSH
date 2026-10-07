using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Services;
using SharpMUSH.Tests.Server;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// Every write of the options document reads it, changes part of it and stores it whole. Writes made at the same
/// moment must each keep the others' changes: <c>@config/set restrict_command</c> once stored the document outside
/// the writer's lock and erased a package setting written alongside it.
/// </summary>
public class ConfigOptionWriterConcurrencyTests
{
	/// <summary>Keeps the last value written under each name, and yields on every read and write, as a database does.</summary>
	private sealed class YieldingStore : IExpandedDataStore
	{
		private readonly Dictionary<string, object> _server = [];

		public ValueTask SetExpandedObjectData(string sharpObjectId, string dataType, dynamic data,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<T?> GetExpandedObjectData<T>(string sharpObjectId, string dataType,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public async ValueTask SetExpandedServerData(string dataType, dynamic data, CancellationToken cancellationToken = default)
		{
			await Task.Yield();
			lock (_server)
			{
				_server[dataType] = data;
			}
		}

		public async ValueTask<T?> GetExpandedServerData<T>(string dataType, CancellationToken cancellationToken = default)
		{
			await Task.Yield();
			lock (_server)
			{
				return _server.TryGetValue(dataType, out var data) ? (T?)data : default;
			}
		}
	}

	[Test]
	public async Task Writes_at_the_same_moment_each_keep_the_others_changes()
	{
		var defaults = TestSharpMushOptions.Create();
		var options = new TestSharpMushOptions.FixedWrapper(defaults);
		var writer = StoreConfigOptionWriter.Create(new YieldingStore(), options, new ConfigurationReloadService());
		var names = Enumerable.Range(0, 40).Select(i => $"banned{i}").ToArray();

		var updates = names.Select(name => Task.Run(async () =>
			await writer.UpdateAsync(current => current with
			{
				BannedNames = new BannedNamesOptions([.. current.BannedNames.BannedNames, name])
			})));
		var set = Task.Run(async () => (await writer.SetAsync(nameof(NetOptions.MudName), "Racing")).Expect<SharpMUSHOptions>());
		await Task.WhenAll([.. updates, set]);

		var stored = await writer.CurrentAsync();
		await Assert.That(stored.BannedNames.BannedNames).IsEquivalentTo([.. defaults.BannedNames.BannedNames, .. names]);
		await Assert.That(stored.Net.MudName).IsEqualTo("Racing");
	}
}
