namespace SharpMUSH.Database.Models;

public record SharpAttributeFlagQueryResult(string Id, string Key, string Name, string Symbol, bool System);

public record SharpAttributeFlagCreationRequest(string Name, string Symbol, bool System);