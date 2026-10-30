namespace SharpMUSH.Library.ExpandedObjectData;

/// <summary>
/// Who scheduled an object's destruction, as an objid: set by <c>@destroy</c> and read when the purge frees the
/// object onto the recover list, so the entry can say who destroyed it.
/// </summary>
[Serializable]
public record DestroyedByData(string? Objid) : AbstractExpandedData
{
	public DestroyedByData() : this((string?)null) { }
}
