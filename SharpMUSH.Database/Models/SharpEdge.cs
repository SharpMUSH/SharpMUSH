namespace SharpMUSH.Database.Models;

public record SharpEdgeQueryResult(string Id, string Key, string From, string To);

public record SharpEdgeCreateRequest(string From, string To);

public record SharpHookEdgeQueryResult(string Id, string Key, string From, string To, string Type);

public record SharpHookEdgeCreateRequest(string From, string To);
