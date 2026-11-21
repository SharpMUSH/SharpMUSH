using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>The read and write keys of attribute table entries (<see cref="AttributeLockKind"/>).</summary>
public interface IAttributeLockStore
{
	/// <summary>Sets one key of the attribute table's entry <paramref name="name"/>, or takes it off; the entry must exist.</summary>
	ValueTask<Found<SharpAttributeEntry>> SetAttributeEntryLockAsync(string name, AttributeLockKind kind, string? key,
		CancellationToken cancellationToken = default);
}
