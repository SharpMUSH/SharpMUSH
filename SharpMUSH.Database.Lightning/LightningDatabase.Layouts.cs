using OneOf;
using OneOf.Types;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="Library.Services.Interfaces.ILayoutRegistryService"/>: admin-customized portal layouts.
/// Ported from <c>SurrealDatabase.Layouts.cs</c>: the whole <see cref="LayoutConfiguration"/> is stored
/// as a single JSON blob (via <see cref="LayoutSerialization"/>) keyed by scope, so no record type or
/// JSON-context registration is needed for this area.
/// </summary>
public partial class LightningDatabase
{
	public async Task UpsertLayoutAsync(string scope, LayoutConfiguration layout)
		=> await Store.WriteAsync(tx => tx.Put(Tables.Layout, Keys.Str(scope), Keys.Str(LayoutSerialization.Serialize(layout))));

	public Task<OneOf<LayoutConfiguration, NotFound>> GetLayoutAsync(string scope)
	{
		var layout = Store.Read(tx => tx.TryGet(Tables.Layout, Keys.Str(scope), out var bytes)
			? LayoutSerialization.Deserialize(Keys.ReadStr(bytes))
			: null);
		return Task.FromResult<OneOf<LayoutConfiguration, NotFound>>(layout is null ? new NotFound() : layout);
	}

	public Task<IReadOnlyList<string>> GetCustomizedScopesAsync()
	{
		var scopes = Store.Read(tx => tx.Range(Tables.Layout, [])
			.Select(e => Keys.ReadStr(e.Key))
			.OrderBy(s => s, StringComparer.Ordinal)
			.ToList());
		return Task.FromResult<IReadOnlyList<string>>(scopes);
	}

	public async Task RemoveLayoutAsync(string scope)
		=> await Store.WriteAsync(tx => tx.Delete(Tables.Layout, Keys.Str(scope)));
}
