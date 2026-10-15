using SharpMUSH.Library.Services.DatabaseConversion.Rhost;

namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>The server a database was written by.</summary>
public enum DatabaseDialect
{
	PennMUSH,
	RhostMUSH
}

/// <summary>The format an import reads its database in: one chosen, or <see cref="Auto"/> to tell from the file.</summary>
public enum DatabaseImportFormat
{
	/// <summary>Tell from the file's first lines (<see cref="DatabaseFormatSniffer"/>).</summary>
	Auto,

	/// <summary>A PennMUSH outdb.</summary>
	PennMUSH,

	/// <summary>A RhostMUSH flatfile, as <c>@dump/flat</c> writes it.</summary>
	RhostMUSH
}

/// <summary>
/// The files one import reads: the database, and whatever came with it. Which side files mean anything
/// depends on the format: a PennMUSH import reads a maildb and a chatdb, a RhostMUSH import its mail
/// dump and the folder dump that goes with it.
/// </summary>
/// <param name="Database">The database file.</param>
/// <param name="Mail">A PennMUSH maildb, or RhostMUSH's <c>.dump.mail</c> from <c>wmail/unload</c>.</param>
/// <param name="Chat">A PennMUSH chatdb. RhostMUSH has none.</param>
/// <param name="MailFolders">RhostMUSH's <c>.dump.folder</c>, which says which folder each message is in.</param>
/// <param name="Format">The database's format, or <see cref="DatabaseImportFormat.Auto"/>.</param>
public sealed record DatabaseImportFiles(
	string Database,
	string? Mail = null,
	string? Chat = null,
	string? MailFolders = null,
	DatabaseImportFormat Format = DatabaseImportFormat.Auto);

/// <summary>Tells a PennMUSH outdb from a RhostMUSH flatfile by the lines they start with.</summary>
public static class DatabaseFormatSniffer
{
	/// <summary>
	/// The format of the text: a RhostMUSH flatfile starts <c>+V</c> and follows it with <c>+S</c>,
	/// <c>+N</c> or <c>+A</c> header lines (or straight with an object, <c>!</c>); a PennMUSH outdb's
	/// <c>+V</c> line is followed by its <c>dbversion</c> or <c>savedtime</c> label. Anything else is
	/// left to the PennMUSH reader, whose errors say what it expected.
	/// </summary>
	public static DatabaseDialect Sniff(TextReader reader)
	{
		var first = reader.ReadLine();
		var second = reader.ReadLine();
		if (first is null || !first.StartsWith("+V", StringComparison.Ordinal) || second is null)
		{
			return DatabaseDialect.PennMUSH;
		}

		return second.Length > 1 && second[0] == '+' && second[1] is 'S' or 'N' or 'A'
			|| second.StartsWith('!')
			? DatabaseDialect.RhostMUSH
			: DatabaseDialect.PennMUSH;
	}

	/// <summary>The format of the file at <paramref name="path"/>.</summary>
	public static DatabaseDialect SniffFile(string path)
	{
		using var reader = new StreamReader(path, RhostFlatfileReader.FileEncoding);
		return Sniff(reader);
	}

	/// <summary>The format to read the file in: the one asked for, or the one its first lines show.</summary>
	public static DatabaseDialect Resolve(DatabaseImportFiles files) => files.Format switch
	{
		DatabaseImportFormat.PennMUSH => DatabaseDialect.PennMUSH,
		DatabaseImportFormat.RhostMUSH => DatabaseDialect.RhostMUSH,
		_ => SniffFile(files.Database)
	};
}
