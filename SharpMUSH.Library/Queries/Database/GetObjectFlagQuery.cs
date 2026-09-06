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

/// <summary>
/// An object's flag set, keyed by its stable graph id (Type is fixed per object). This is the read
/// behind <c>SharpObject.Flags</c>, so it answers every <c>HasFlag</c> / <c>IsWizard</c> check the
/// parser makes - including the DEBUG check on every function call - from cache rather than from
/// a graph traversal per call.
/// </summary>
/// <remarks>
/// Invalidated by key from <c>SetObjectFlagCommand</c> / <c>UnsetObjectFlagCommand</c>, and by the
/// <see cref="Definitions.CacheTags.ObjectFlags"/> tag from <c>DeleteObjectCommand</c>: dbref numbers
/// are reused, so an object created into a vacated number must not inherit its predecessor's flags.
/// </remarks>
public record GetObjectFlagsQuery(string Id, string Type) : IStreamQuery<SharpObjectFlag>, ICacheable
{
	public string CacheKey => CacheKeys.ObjectFlags(Id);
	public string[] CacheTags => [Definitions.CacheTags.ObjectFlags];
}

public record GetAllObjectFlagsQuery() : IStreamQuery<SharpObjectFlag>, ICacheable
{
	public string CacheKey => "global:ObjectFlagsList";
	public string[] CacheTags => [Definitions.CacheTags.FlagList];
}
