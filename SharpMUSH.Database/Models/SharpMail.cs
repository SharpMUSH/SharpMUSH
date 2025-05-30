﻿namespace SharpMUSH.Database.Models;

public record SharpMailCreateRequest(
	long DateSent,
	bool Fresh,
	bool Read,
	bool Tagged,
	bool Urgent,
	bool Forwarded,
	bool Cleared,
	string Folder,
	string Content,
	string Subject
	);

public record SharpMailQueryResult(
	string Id,
	string Key,
	long DateSent,
	bool Fresh,
	bool Read,
	bool Tagged,
	bool Urgent,
	bool Forwarded,
	bool Cleared,
	string Folder,
	string Content,
	string Subject);