namespace SharpMUSH.Database.Models;

public record SharpAttributeQueryResult(string Id, string Key, string Name, string[] Flags, string Value, string LongName)
{
	// Optional: flags as objects when fetched with flag details from AQL query
	public List<SharpAttributeFlagQueryResult>? flags { get; init; }
}

public record SharpAttributeCreateRequest(string Name, string Value, string LongName);