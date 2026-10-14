namespace SharpMUSH.Library.Models;

/// <summary>
/// A model relation a provider can read on the spot, without an async enumeration: each
/// <see cref="ReadNow"/> is a fresh read, exactly what enumerating the relation would return.
/// </summary>
/// <remarks>
/// An object's flags are read for every <c>hasflag</c>, every inherited attribute (the ORPHAN check) and
/// every item a room listing shows. Enumerating them asynchronously built an iterator, an enumerator
/// and a list per read around a lookup that completes synchronously in the embedded provider.
/// </remarks>
public interface IReadableNow<out T>
{
	IReadOnlyList<T> ReadNow();
}
