using System.Globalization;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.DatabaseConversion.TinyMux;

/// <summary>
/// Reads TinyMUX's <c>mail.db</c> (versions 5 and 6) into the mail model the importer converts.
/// </summary>
/// <remarks>
/// <para>The file is <c>+V6</c> (UTF-8; <c>+V5</c> is Latin-1), the highest message number, then one
/// entry per delivered message: recipient, sender and body number on lines of their own, then the
/// quoted recipient list, time sent and subject, then the read word. <c>*** END OF DUMP ***</c> ends
/// them; the bodies follow as number and quoted text pairs up to <c>+++ END OF DUMP +++</c>; then
/// <c>*** Begin MALIAS ***</c>, a count, and per alias <c>owner members</c>, <c>N:name</c>,
/// <c>D:description</c> and one member per line.</para>
/// <para>The read word's bits are PennMUSH's (read, cleared, urgent, mass, safe, tag, forward, folder in
/// bits 8-11). A TinyMUX alias owned by God may be used and seen by everyone; any other only by its owner
/// and staff.</para>
/// </remarks>
public static class TinyMuxMailReader
{
	public static async Task<PennMUSHMailDatabase> ReadFileAsync(string path, CancellationToken cancellationToken = default)
	{
		await using var stream = File.OpenRead(path);
		var first = TinyMuxFlatfileReader.FirstLine(stream);
		stream.Position = 0;
		using var text = new StreamReader(stream, TinyMuxFlatfileReader.EncodingFor(first.StartsWith("+V5", StringComparison.Ordinal)), false);
		return await Task.Run(() => Read(text, Path.GetFileName(path)), cancellationToken);
	}

	public static PennMUSHMailDatabase Read(TextReader text, string description = "mail.db", int godPlayer = 1)
	{
		var reader = new TinyMuxFlatfileReader(text, description);
		var header = reader.ReadLine();
		if (header is not ("+V6" or "+V5"))
		{
			throw reader.Error($"'{header}' is not a mail.db this importer reads (+V5 or +V6)");
		}

		reader.ReadInt();
		var entries = new List<(int To, int From, int Body, string Time, string Subject, int Flags)>();
		string line;
		while (!(line = reader.ReadLine() ?? throw reader.Error("the file ends before *** END OF DUMP ***"))
			.StartsWith("***", StringComparison.Ordinal))
		{
			var to = Number(reader, line);
			var from = reader.ReadInt();
			var body = reader.ReadInt();
			reader.ReadString();
			var time = reader.ReadString();
			var subject = reader.ReadString();
			var flags = reader.ReadInt();
			entries.Add((to, from, body, time, subject, flags));
		}

		var bodies = new Dictionary<int, string>();
		while (!(line = reader.ReadLine() ?? throw reader.Error("the file ends before +++ END OF DUMP +++"))
			.StartsWith("+++", StringComparison.Ordinal))
		{
			bodies[Number(reader, line)] = reader.ReadString();
		}

		var mail = new PennMUSHMailDatabase
		{
			Flags = PennMUSHMailDatabase.SubjectFlag | PennMUSHMailDatabase.AliasesFlag,
			MessageCount = entries.Count
		};

		foreach (var (to, from, body, time, subject, flags) in entries)
		{
			var bodyText = bodies.TryGetValue(body, out var found)
				? found
				: throw reader.Error($"a message to #{to} names body {body}, which the file does not have");
			mail.Messages.Add(new PennMUSHMailMessage(to, from, 0, PennTime(time), subject, bodyText, flags));
		}

		if (reader.ReadLine() == "*** Begin MALIAS ***")
		{
			ReadAliases(reader, mail, godPlayer);
		}

		return mail;
	}

	private static void ReadAliases(TinyMuxFlatfileReader reader, PennMUSHMailDatabase mail, int godPlayer)
	{
		var count = reader.ReadInt();
		for (var i = 0; i < count; i++)
		{
			var ownerLine = reader.ReadLine() ?? throw reader.Error("the file ends inside the mail aliases");
			var parts = ownerLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			var owner = parts.Length > 0 ? Number(reader, parts[0]) : 0;
			var members = parts.Length > 1 ? Number(reader, parts[1]) : 0;
			var name = Field(reader.ReadLine(), "N:");
			var aliasDescription = Field(reader.ReadLine(), "D:");
			var list = new List<int>();
			for (var j = 0; j < members; j++)
			{
				list.Add(reader.ReadInt());
			}

			var privileges = owner == godPlayer
				? (int)MailAliasPrivileges.Everyone
				: (int)(MailAliasPrivileges.Owner | MailAliasPrivileges.Admin);
			mail.Aliases.Add(new PennMUSHMailAlias(owner, name.TrimStart('*'), aliasDescription, privileges, privileges, list));
		}
	}

	private static string Field(string? line, string prefix)
		=> line is not null && line.StartsWith(prefix, StringComparison.Ordinal) ? line[prefix.Length..] : line ?? string.Empty;

	private static int Number(TinyMuxFlatfileReader reader, string text)
		=> int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
			? number
			: throw reader.Error($"'{text}' is not a number");

	/// <summary>The time sent in the form the PennMUSH importer reads; unchanged when it cannot be read.</summary>
	private static string PennTime(string time)
		=> TinyMuxDatabaseReader.TryParseTime(time, out var parsed)
			? parsed.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture)
			: time;

	/// <summary>Colour in subjects and bodies, written as ANSI for the importer to read.</summary>
	public static PennMUSHMailDatabase WithColour(PennMUSHMailDatabase mail, int version)
	{
		var messages = mail.Messages.Select(message => message with
		{
			Subject = TinyMuxColour.ToAnsi(message.Subject, version),
			Body = TinyMuxColour.ToAnsi(message.Body, version)
		}).ToList();
		mail.Messages = messages;
		return mail;
	}
}
