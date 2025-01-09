namespace SharpMUSH.Database.Models;

public record SharpMailQueryResult(
	string Id,
	string Key,
	int DateSent,
	bool? Fresh,
	bool? Read,
	bool? Tagged,
	bool? Urgent,
	bool? Cleared,
	string Folder,
	string Content,
	string Subject);