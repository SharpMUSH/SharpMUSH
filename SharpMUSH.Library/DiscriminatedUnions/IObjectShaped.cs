using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A value that names one object and can be rebuilt from that object's node. The caching
/// behaviours store such a value as the object id it names and resolve it through the object node
/// cache on every read, so every cached result hands out the one instance of an object in the
/// process. Each type says for itself which node it accepts: content refuses a room, a container
/// refuses an exit, an optional accepts nothing.
/// </summary>
public interface IObjectShaped<TSelf> where TSelf : IObjectShaped<TSelf>
{
	/// <summary>
	/// The full object id <paramref name="value"/> names, number and creation milliseconds, or null
	/// when it names none. The milliseconds come from the loaded object, so a recycled number can
	/// never resolve to the object that took its place.
	/// </summary>
	static abstract DBRef? RefOf(TSelf value);

	/// <summary>
	/// The value for a node the cache resolved, or false when the node cannot be one: the object is
	/// gone, or is not of the kind this value carries.
	/// </summary>
	static abstract bool TryFromNode(AnyOptionalSharpObject node, out TSelf value);
}
