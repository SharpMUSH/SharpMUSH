namespace SharpMUSH.Library.Models.Wiki;

/// <summary>Where a store's fact index is.</summary>
public enum WikiFactIndexPhase
{
	/// <summary>The index is being emptied, after the schema changed or a rebuild was asked for; then it builds.</summary>
	Clearing,

	/// <summary>The index is being filled, a batch of pages at a time; every page write already keeps it.</summary>
	Building,

	/// <summary>Every page is in the index.</summary>
	Ready
}

/// <summary>A fact index's phase, and how many of the wiki's <paramref name="Pages"/> it holds.</summary>
public sealed record WikiFactIndexStatus(WikiFactIndexPhase Phase, long Indexed, long Pages)
{
	public bool Ready => Phase == WikiFactIndexPhase.Ready;
}
