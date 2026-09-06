namespace SharpMUSH.Database.Models;

/// <param name="FlagDocs">
/// The attribute's flag documents when the query projected them alongside the attribute (see the
/// ArangoDB provider's <c>AttributeWithFlags</c>); null when it returned the bare document.
/// </param>
public record SharpAttributeQueryResult(string Id, string Key, string Name, string[] Flags, string Value, string LongName,
	SharpAttributeFlagQueryResult[]? FlagDocs = null);

public record SharpAttributeCreateRequest(string Name, string Value, string LongName);