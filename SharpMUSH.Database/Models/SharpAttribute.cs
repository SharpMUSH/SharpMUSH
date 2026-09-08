namespace SharpMUSH.Database.Models;

/// <param name="FlagDocs">
/// The attribute's flag documents when the query projected them alongside the attribute (see the
/// </param>
public record SharpAttributeQueryResult(string Id, string Key, string Name, string[] Flags, string Value, string LongName,
	SharpAttributeFlagQueryResult[]? FlagDocs = null);

public record SharpAttributeCreateRequest(string Name, string Value, string LongName);