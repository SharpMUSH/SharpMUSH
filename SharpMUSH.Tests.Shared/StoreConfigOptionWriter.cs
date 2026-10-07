using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Shared;

/// <summary>
/// The production <see cref="ConfigOptionWriter"/> over an <see cref="IExpandedDataStore"/>, for a test that builds a
/// controller or service by hand around a substituted or in-memory store: what the writer stores reaches the store
/// under the name the options are kept under, and its lock is the real one.
/// </summary>
public static class StoreConfigOptionWriter
{
	public static ConfigOptionWriter Create(IExpandedDataStore store, IOptionsWrapper<SharpMUSHOptions> options,
		ConfigurationReloadService reload)
		=> new(new StoreServerData(store), options, [], reload, NullLogger<ConfigOptionWriter>.Instance);

	private sealed class StoreServerData(IExpandedDataStore store) : IExpandedObjectDataService
	{
		public ValueTask<T?> GetExpandedDataAsync<T>(SharpObject obj) where T : class
			=> throw new NotSupportedException();

		public ValueTask SetExpandedDataAsync<T>(T data, SharpObject obj, bool ignoreNull = false) where T : class
			=> throw new NotSupportedException();

		public ValueTask<T?> GetExpandedServerDataAsync<T>() where T : class
			=> store.GetExpandedServerData<T>(typeof(T).Name);

		public ValueTask SetExpandedServerDataAsync<T>(T data, bool ignoreNull = false) where T : class
			=> store.SetExpandedServerData(typeof(T).Name, data);
	}
}
