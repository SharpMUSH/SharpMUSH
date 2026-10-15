using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Indexes on attribute values (<c>@attribute/index</c>): which objects hold a value of one attribute, in the
/// value's order. An index lives on the attribute entry and lists each object's own copy, never an inherited one.
/// </summary>
/// <remarks>
/// The provider keeps every index in the same write as the value at every place a value is written, cleared, wiped
/// or destroyed, so a write either lands in both or in neither. An empty value, and a copy flagged <c>internal</c>,
/// have no entry. A value is kept to a bounded length in the index; an equality probe that reaches it is checked
/// against the stored value.
/// </remarks>
public interface IAttributeIndexStore
{
	/// <summary>
	/// Puts an index with <paramref name="options"/> on the entry <paramref name="name"/> and marks it building from
	/// the first object, dropping whatever an earlier index with other options had kept. An index that already has
	/// these options is left as it is. Refused when the table has no such entry, or its default flags include
	/// <c>internal</c>.
	/// </summary>
	ValueTask<Result<SharpAttributeEntry>> SetAttributeIndexAsync(string name, AttributeIndexOptions options,
		CancellationToken cancellationToken = default);

	/// <summary>Takes the index off the entry and removes all its entries; false when it had none.</summary>
	ValueTask<bool> DropAttributeIndexAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>
	/// Indexes the copies on the next <paramref name="batchSize"/> objects of a building index, in one write, and moves
	/// its resume point past them, marking it ready after the last. Batches re-index idempotently, so a value written
	/// between two batches is never lost. Not found when the entry has no index; a ready index is a step that does
	/// nothing.
	/// </summary>
	ValueTask<Found<AttributeIndexBuildStep>> BuildAttributeIndexBatchAsync(string name, int batchSize,
		CancellationToken cancellationToken = default);

	/// <summary>One page of matches, in index order (ties in dbref order).</summary>
	ValueTask<IndexedAttributeResult> QueryAttributeIndexAsync(IndexedAttributeQuerySpec query,
		CancellationToken cancellationToken = default);

	/// <summary>How many objects the index lists under a value equal to <paramref name="value"/>, every one counted.</summary>
	ValueTask<IndexedAttributeCount> CountAttributeIndexAsync(string name, string value, CancellationToken cancellationToken = default);

	/// <summary>Every index, with its size.</summary>
	IReadOnlyList<AttributeIndexUsage> GetAttributeIndexUsage();

	/// <summary>
	/// How many objects (of <paramref name="type"/>, or of every type when null) hold a copy of the attribute
	/// <paramref name="name"/> of their own: what a build of an index on it would index.
	/// </summary>
	ValueTask<long> CountAttributeCopiesAsync(string name, string? type, CancellationToken cancellationToken = default);
}
