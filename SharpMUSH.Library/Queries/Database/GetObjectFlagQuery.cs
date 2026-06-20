using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetObjectFlagQuery(string FlagName) : IQuery<SharpObjectFlag?>, ICacheable
{
	public string CacheKey => $"flag-definition:{FlagName}";
	// The @flag commands (Create/Delete/Disable) already invalidate the FlagList tag, so tagging here
	// makes a flag-definition lookup invalidate whenever the flag table changes — no new wiring needed.
	public string[] CacheTags => [Definitions.CacheTags.FlagList];
}

public record GetObjectFlagsQuery(string Id, string Type) : IStreamQuery<SharpObjectFlag>, ICacheable
{
	// An object's flag set, keyed by its stable graph _id (Type is fixed per object). Invalidated by
	// SetObjectFlagCommand / UnsetObjectFlagCommand.
	public string CacheKey => CacheKeys.ObjectFlags(Id);
	public string[] CacheTags => [];
}

public record GetAllObjectFlagsQuery() : IStreamQuery<SharpObjectFlag>, ICacheable
{
	public string CacheKey => "global:ObjectFlagsList";
	public string[] CacheTags => [Definitions.CacheTags.FlagList];
}
