using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Navigation;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="Library.IPortalNavigationStore"/>: the portal navigation layout, one JSON row. A save checks the
/// stored version inside its own write, so of two saves read at the same version only the first lands.
/// </summary>
public partial class LightningDatabase
{
	private static byte[] NavLayoutKey => Keys.Str("main");

	public ValueTask<Found<NavLayout>> GetNavLayoutAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var layout = Store.Read(tx => tx.TryGet(Tables.NavLayout, NavLayoutKey, out var bytes)
			? Codec.Deserialize<NavLayout>(bytes)
			: null);
		return ValueTask.FromResult<Found<NavLayout>>(layout is null ? new NotFound() : layout);
	}

	public async ValueTask<NavLayoutSaveResult> SaveNavLayoutAsync(NavLayout layout, long expectedVersion,
		CancellationToken cancellationToken = default)
	{
		// Freeze the caller's lists before queueing: the write must store what was checked.
		var snapshot = layout with
		{
			Groups = [.. layout.Groups.Select(g => g with { Items = [.. g.Items] })],
			Hidden = [.. layout.Hidden],
			MobileTabs = [.. layout.MobileTabs],
		};
		if (NavLayoutRules.Problem(snapshot) is { } problem) return new Error<string>(problem);
		return await Store.WriteAsync<NavLayoutSaveResult>(tx =>
		{
			var current = tx.TryGet(Tables.NavLayout, NavLayoutKey, out var bytes) ? Codec.Deserialize<NavLayout>(bytes).Version : 0;
			if (current != expectedVersion) return new NavLayoutConflict(current);
			var saved = snapshot with { Version = expectedVersion + 1 };
			tx.Put(Tables.NavLayout, NavLayoutKey, Codec.Serialize(saved));
			return saved;
		}, cancellationToken);
	}

	public async ValueTask ResetNavLayoutAsync(CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx => tx.Delete(Tables.NavLayout, NavLayoutKey), cancellationToken);
}
