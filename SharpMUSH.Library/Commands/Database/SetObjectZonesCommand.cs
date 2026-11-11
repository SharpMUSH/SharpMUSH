using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Puts <paramref name="Target"/> in exactly <paramref name="Zones"/>, the first as its zone; none clears them. Checks
/// nothing: the caller has decided who may. See <see cref="IZoneListStore"/>.
/// </summary>
public record SetObjectZonesCommand(AnySharpObject Target, IReadOnlyList<AnySharpObject> Zones) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys =>
	[
		Definitions.CacheKeys.Object(Target.Object().DBRef),
		.. Zones.Select(zone => Definitions.CacheKeys.Object(zone.Object().DBRef))
	];

	public string[] CacheTags => [Definitions.CacheTags.ZoneObjects];
}
