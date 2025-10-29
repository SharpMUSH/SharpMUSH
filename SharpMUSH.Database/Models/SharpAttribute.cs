namespace SharpMUSH.Database.Models;

public record SharpAttributeQueryResult(string Id, string Key, string Name, string[] Flags, string Value, string LongName);

public record SharpAttributeCreateRequest(string Name, string Value, string LongName);