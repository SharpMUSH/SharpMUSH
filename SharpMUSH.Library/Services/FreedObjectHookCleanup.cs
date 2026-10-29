using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>Drops the <c>@hook</c>s that run code on freed objects, which would otherwise run on their numbers' next holders.</summary>
public class FreedObjectHookCleanup(IHookService hooks) : IFreedObjectCleanup
{
	// By number: a hook may have been set with a bare dbref, and the object it named is gone either way.
	public async ValueTask ObjectsFreedAsync(IReadOnlyCollection<DBRef> objids, CancellationToken cancellationToken = default)
		=> await hooks.ClearHooksOnAsync([.. objids.Select(objid => new DBRef(objid.Number))]);
}
