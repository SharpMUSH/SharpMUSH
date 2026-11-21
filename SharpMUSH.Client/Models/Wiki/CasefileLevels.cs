namespace SharpMUSH.Client.Models.Wiki;

/// <summary>The member levels of a casefile as the API spells them, lowest first, and the resource key of each label.</summary>
public static class CasefileLevels
{
	public static IReadOnlyList<string> All { get; } = ["reader", "contributor", "lead"];

	public static string ResourceKey(string level) => level switch
	{
		"lead" => "WkCasefileLevelLead",
		"contributor" => "WkCasefileLevelContributor",
		_ => "WkCasefileLevelReader",
	};
}
