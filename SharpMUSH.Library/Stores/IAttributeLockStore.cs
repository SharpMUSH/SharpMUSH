using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>The read and write keys of attributes and of attribute table entries (<see cref="AttributeLockKind"/>).</summary>
public interface IAttributeLockStore
{
	/// <summary>
	/// Sets one key of the attribute <paramref name="attribute"/> names, or takes it off with a null <paramref name="key"/>;
	/// the attribute with its new key, or <see cref="NotFound"/> when it no longer exists.
	/// </summary>
	ValueTask<Found<SharpAttribute>> SetAttributeLockAsync(SharpAttribute attribute, AttributeLockKind kind, string? key,
		CancellationToken cancellationToken = default);

	/// <summary>Sets one key of the attribute table's entry <paramref name="name"/>, or takes it off; the entry must exist.</summary>
	ValueTask<Found<SharpAttributeEntry>> SetAttributeEntryLockAsync(string name, AttributeLockKind kind, string? key,
		CancellationToken cancellationToken = default);
}
