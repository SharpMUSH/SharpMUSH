using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;
using SharpMUSH.Library.Services.DatabaseConversion.TinyMux;

namespace SharpMUSH.Server.Services.Imports;

/// <summary>Reads every declared companion strictly before any target-world writes or configuration changes.</summary>
public sealed class DatabaseImportPreflight(PennMUSHDatabaseParser parser)
{
	public async Task<PennMUSHDatabase> ReadAsync(DatabaseImportFiles files, CancellationToken cancellationToken = default)
	{
		foreach (var path in new[] { files.Database, files.Mail, files.Chat, files.MailFolders }.OfType<string>())
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!File.Exists(path)) throw new FileNotFoundException("A declared import input is missing.", path);
			if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Import inputs must be regular immutable copies, not symbolic links.");
		}
		var dialect = DatabaseFormatSniffer.Resolve(files);
		if (files.MailFolders is not null && (files.Mail is null || dialect != DatabaseDialect.RhostMUSH))
			throw new FormatException("A declared folder dump requires a RhostMUSH mail dump.");
		if (files.Chat is not null && dialect == DatabaseDialect.RhostMUSH)
			throw new FormatException("RhostMUSH has no supported chat companion; the declared input cannot be silently omitted.");
		PennMUSHDatabase database;
		if (dialect == DatabaseDialect.RhostMUSH)
		{
			var source = await RhostFlatfileReader.ReadFileStrictAsync(files.Database, cancellationToken);
			var mail = files.Mail is null ? null : await RhostMailReader.ReadFilesAsync(files.Mail, files.MailFolders, cancellationToken);
			database = RhostDatabaseTranslator.Translate(source, mail);
		}
		else if (dialect == DatabaseDialect.TinyMUX)
		{
			database = await TinyMuxDatabaseReader.ReadFileAsync(files.Database, cancellationToken);
			var version = TinyMuxDatabaseReader.FlatfileVersion(database);
			if (files.Mail is not null) database.Mail = TinyMuxMailReader.WithColour(await TinyMuxMailReader.ReadFileAsync(files.Mail, cancellationToken), version);
			if (files.Chat is not null) TinyMuxComsysReader.Apply(database, await TinyMuxComsysReader.ReadFileAsync(files.Chat, cancellationToken), version);
		}
		else
		{
			database = await parser.ParseFileAsync(files.Database, cancellationToken);
			if (files.Mail is not null) database.Mail = await parser.ParseMailFileAsync(files.Mail, cancellationToken);
			if (files.Chat is not null) database.Chat = await parser.ParseChatFileAsync(files.Chat, cancellationToken);
		}
		if (database.Mail.ReadError is not null || database.Mail.MessageReadError is not null || database.Chat.ReadError is not null)
			throw new FormatException("A declared ancillary store is malformed or incomplete; no import will be written.");
		if (database.Objects.Count == 0) throw new FormatException("An empty source world cannot replace a live world.");
		var numbers = database.Objects.Select(obj => obj.DBRef).ToHashSet();
		if (numbers.Count != database.Objects.Count) throw new FormatException("Source object numbers are duplicated.");
		foreach (var obj in database.Objects)
			foreach (var reference in new[] { obj.Location, obj.Owner, obj.Parent, obj.Zone, obj.Link })
				if (reference >= 0 && !numbers.Contains(reference))
					throw new FormatException($"Source #{obj.DBRef} references missing object #{reference}.");
		return database;
	}
}
