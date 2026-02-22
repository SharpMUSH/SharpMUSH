namespace SharpMUSH.Library.ExpandedObjectData;

/// <summary>
/// Data class for storing suggestion/spell-check categories and vocabulary.
/// Used for suggesting alternatives to misspelled words (help entries, function names, etc.)
/// </summary>
[Serializable]
public record SuggestionData : AbstractExpandedData
{
	public Dictionary<string, HashSet<string>>? Categories { get; init; }

	/// <summary>
	/// Default constructor with empty categories.
	/// </summary>
	public SuggestionData() : this(new Dictionary<string, HashSet<string>>()) { }

	/// <summary>
	/// Constructor with categories.
	/// </summary>
	public SuggestionData(Dictionary<string, HashSet<string>>? categories)
	{
		Categories = categories ?? new Dictionary<string, HashSet<string>>();
	}
}
