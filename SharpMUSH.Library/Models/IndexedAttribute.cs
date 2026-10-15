namespace SharpMUSH.Library.Models;

/// <summary>What an <c>indexed()</c> query asks of an attribute index.</summary>
public enum IndexedAttributeOperation
{
	/// <summary>Values equal to the probe, in the index's fold.</summary>
	Equal,

	/// <summary>Values starting with the probe.</summary>
	Prefix,

	/// <summary>Values below the probe.</summary>
	Less,

	/// <summary>Values at or below the probe.</summary>
	LessOrEqual,

	/// <summary>Values above the probe.</summary>
	Greater,

	/// <summary>Values at or above the probe.</summary>
	GreaterOrEqual,

	/// <summary>Values from the probe to the upper bound, both included.</summary>
	Between,

	/// <summary>Every value, lowest first.</summary>
	First,

	/// <summary>Every value, highest first.</summary>
	Last
}

/// <summary>
/// One page of an index query. <paramref name="After"/> resumes after the last row of the previous page;
/// <paramref name="Facts"/> asks for what <see cref="IndexedAttributeVisibility"/> needs on each hit, which a privileged
/// caller does without.
/// </summary>
/// <param name="Name">The attribute's long name, upper case.</param>
/// <param name="Operation">What to match.</param>
/// <param name="Value">The probe, as typed (the index folds it).</param>
/// <param name="UpperValue">The upper bound of <see cref="IndexedAttributeOperation.Between"/>; otherwise null.</param>
/// <param name="After">Where the previous page stopped; null for the first page.</param>
/// <param name="Limit">The most rows this page reads.</param>
/// <param name="Facts">Whether each hit carries <see cref="IndexedAttributeHit.Facts"/>.</param>
public sealed record IndexedAttributeQuerySpec(
	string Name,
	IndexedAttributeOperation Operation,
	string Value,
	string? UpperValue,
	IndexedAttributeCursor? After,
	int Limit,
	bool Facts);

/// <summary>An opaque position in an index: the row's key and the object it names.</summary>
public sealed record IndexedAttributeCursor(byte[] Key, long Dbref);

/// <summary>
/// What the store knows, cheaply, about one hit and the copy that put it there: enough to decide most hits' visibility
/// without loading the object (<see cref="IndexedAttributeVisibility"/>).
/// </summary>
[Flags]
public enum IndexedAttributeFacts
{
	None = 0,

	/// <summary>The copy is flagged <c>internal</c>.</summary>
	Internal = 1 << 0,

	/// <summary>The copy or a branch above it is <c>mortal_dark</c>.</summary>
	MortalDark = 1 << 1,

	/// <summary>The copy and every branch above it are <c>visual</c>.</summary>
	AllVisual = 1 << 2,

	/// <summary>The copy or a branch above it is <c>nearby</c>.</summary>
	AnyNearby = 1 << 3,

	/// <summary>The object has the <c>VISUAL</c> flag (its examine lock decides).</summary>
	ObjectVisual = 1 << 4,

	/// <summary>The object is in a zone.</summary>
	HasZone = 1 << 5,

	/// <summary>The object's owner has the <c>SHARED</c> flag (a zone master player).</summary>
	OwnerShared = 1 << 6,

	/// <summary>The object has a control lock of its own.</summary>
	HasControlLock = 1 << 7,

	/// <summary>The object is a player.</summary>
	IsPlayer = 1 << 8
}

/// <summary>One object an index query found.</summary>
/// <param name="Number">Its dbref number.</param>
/// <param name="CreationTime">Its creation time, for its objid; 0 when the query did not ask for facts.</param>
/// <param name="Owner">Its owner's dbref number; -1 when the query did not ask for facts.</param>
/// <param name="Facts">What decides most of its visibility; <see cref="IndexedAttributeFacts.None"/> when not asked for.</param>
public readonly record struct IndexedAttributeHit(int Number, long CreationTime, int Owner, IndexedAttributeFacts Facts)
{
	/// <summary>The object, with its creation time when the query read it.</summary>
	public DBRef DBRef => CreationTime > 0 ? new DBRef(Number, CreationTime) : new DBRef(Number);
}

/// <summary>A page of hits in index order, and where the next page starts (null when there is none).</summary>
public sealed record IndexedAttributePage(IReadOnlyList<IndexedAttributeHit> Hits, IndexedAttributeCursor? Next);

/// <summary>The attribute has no index, or no attribute entry.</summary>
public readonly record struct NotIndexed;

/// <summary>The attribute's index is still being built.</summary>
public readonly record struct IndexBuilding;

/// <summary>A page of an index query, or why the index cannot answer.</summary>
public union IndexedAttributeResult(IndexedAttributePage, NotIndexed, IndexBuilding);

/// <summary>How many objects an index lists under one value, or why the index cannot answer.</summary>
public union IndexedAttributeCount(long, NotIndexed, IndexBuilding);

/// <summary>One batch of an index build.</summary>
/// <param name="Scanned">Objects the batch looked at.</param>
/// <param name="Indexed">Copies of the attribute it indexed.</param>
/// <param name="ResumeAt">The dbref the next batch starts at.</param>
/// <param name="Ready">Whether the build is done.</param>
/// <param name="Total">Objects the whole build looks at, as the batch counted them.</param>
public sealed record AttributeIndexBuildStep(long Scanned, long Indexed, long ResumeAt, bool Ready, long Total);

/// <summary>One attribute index, as <c>@storage</c> reports it.</summary>
/// <param name="Name">The attribute's name.</param>
/// <param name="Options">Its options, as <c>@attribute/index</c> takes them (empty for the default).</param>
/// <param name="Rows">Its entries: one per indexed copy, or per element of a split one.</param>
/// <param name="Pages">The pages it takes, its share of the index tables' pages by bytes.</param>
/// <param name="Bytes">Those pages in bytes.</param>
/// <param name="Ready">Whether it is built.</param>
/// <param name="ResumeAt">While building, the dbref the build has reached.</param>
public sealed record AttributeIndexUsage(string Name, string Options, long Rows, long Pages, long Bytes, bool Ready, long ResumeAt);
