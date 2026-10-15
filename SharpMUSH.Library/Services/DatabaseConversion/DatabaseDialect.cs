using System.Globalization;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;

namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>The server a database was written by.</summary>
public enum DatabaseDialect
{
	PennMUSH,
	RhostMUSH,
	TinyMUX
}

/// <summary>How the import names each server in its messages.</summary>
public static class DatabaseDialectNames
{
	/// <summary>The server's own name, as its players write it.</summary>
	public static string DisplayName(this DatabaseDialect dialect) => dialect switch
	{
		DatabaseDialect.RhostMUSH => "RhostMUSH",
		DatabaseDialect.TinyMUX => "TinyMUX",
		_ => "PennMUSH"
	};
}

/// <summary>The format an import reads its database in: one chosen, or <see cref="Auto"/> to tell from the file.</summary>
public enum DatabaseImportFormat
{
	/// <summary>Tell from the file's first lines (<see cref="DatabaseFormatSniffer"/>).</summary>
	Auto,

	/// <summary>A PennMUSH outdb.</summary>
	PennMUSH,

	/// <summary>A RhostMUSH flatfile, as <c>@dump/flat</c> writes it.</summary>
	RhostMUSH,

	/// <summary>A TinyMUX flatfile, as <c>db_unload</c> or <c>@dump/flat</c> writes it.</summary>
	TinyMUX
}

/// <summary>Reads a <see cref="DatabaseImportFormat"/> as the upload API and the start-up import take it.</summary>
public static class DatabaseImportFormats
{
	/// <summary>
	/// The format named by <paramref name="text"/>, ignoring case; blank is <see cref="DatabaseImportFormat.Auto"/>.
	/// A number is refused, though <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/> would take one.
	/// </summary>
	public static bool TryParse(string? text, out DatabaseImportFormat format)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			format = DatabaseImportFormat.Auto;
			return true;
		}

		return Enum.TryParse(text, ignoreCase: true, out format) && Enum.IsDefined(format)
			&& !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
	}
}

/// <summary>
/// The files one import reads: the database, and whatever came with it. Which side files mean anything
/// depends on the format: a PennMUSH import reads a maildb and a chatdb, a RhostMUSH import its mail
/// dump and the folder dump that goes with it, a TinyMUX import its <c>mail.db</c> and <c>comsys.db</c>.
/// </summary>
/// <param name="Database">The database file.</param>
/// <param name="Mail">A PennMUSH maildb, RhostMUSH's <c>.dump.mail</c> from <c>wmail/unload</c>, or TinyMUX's
/// <c>mail.db</c>.</param>
/// <param name="Chat">A PennMUSH chatdb, or TinyMUX's <c>comsys.db</c>. RhostMUSH has none.</param>
/// <param name="MailFolders">RhostMUSH's <c>.dump.folder</c>, which says which folder each message is in.</param>
/// <param name="Format">The database's format, or <see cref="DatabaseImportFormat.Auto"/>.</param>
public sealed record DatabaseImportFiles(
	string Database,
	string? Mail = null,
	string? Chat = null,
	string? MailFolders = null,
	DatabaseImportFormat Format = DatabaseImportFormat.Auto);

/// <summary>Tells a PennMUSH outdb, a RhostMUSH flatfile and a TinyMUX flatfile apart by the lines they start with.</summary>
public static class DatabaseFormatSniffer
{
	/// <summary>
	/// The format of the text: a TinyMUX flatfile starts <c>+X</c> and a number. A RhostMUSH flatfile starts
	/// <c>+V</c> and follows it with <c>+S</c>, <c>+N</c> or <c>+A</c> header lines (or straight with an object,
	/// <c>!</c>); a PennMUSH outdb's <c>+V</c> line is followed by its <c>dbversion</c> or <c>savedtime</c> label.
	/// Anything else is left to the PennMUSH reader, whose errors say what it expected.
	/// </summary>
	public static DatabaseDialect Sniff(TextReader reader)
	{
		var first = reader.ReadLine();
		if (first is not null && first.StartsWith("+X", StringComparison.Ordinal)
			&& long.TryParse(first.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out _))
		{
			return DatabaseDialect.TinyMUX;
		}

		var second = reader.ReadLine();
		if (first is null || !first.StartsWith("+V", StringComparison.Ordinal) || second is null)
		{
			return DatabaseDialect.PennMUSH;
		}

		return (second.Length > 1 && second[0] == '+' && second[1] is 'S' or 'N' or 'A')
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
		DatabaseImportFormat.TinyMUX => DatabaseDialect.TinyMUX,
		_ => SniffFile(files.Database)
	};
}
