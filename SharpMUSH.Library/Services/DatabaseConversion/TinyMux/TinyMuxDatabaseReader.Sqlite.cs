using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SharpMUSH.Library.Services.DatabaseConversion.TinyMux;

/// <summary>
/// Reads the SQLite database TinyMUX 2.14 runs from (<c>game/data/netmux.sqlite</c>) into the same model
/// as its flatfile, with the comsys and mail it also holds, so one converter writes both.
/// </summary>
/// <remarks>
/// <para>The tables read are <c>objects</c> (one row per object number, with the record's fields as
/// columns), <c>attributes</c> (the value with its own owner and flags), <c>attrnames</c> (the user-named
/// attributes, as the flatfile's <c>+A</c> lines), <c>metadata</c>, <c>channels</c>, <c>channel_users</c>,
/// <c>player_channels</c>, <c>mail_headers</c>, <c>mail_bodies</c> and <c>mail_aliases</c>. The rest
/// (the connection log, compiled code, routes) is TinyMUX's own and is left out.</para>
/// <para>An object's name is its <c>Name</c> attribute, and its money its <c>Money</c> attribute, which is
/// where TinyMUX keeps both. Text is UTF-8 with colour as private-use code points, as in a version 5
/// flatfile. A value may still carry the <c>\x01owner:flags:</c> prefix of the earliest databases, whose
/// attribute rows had no owner or flags of their own.</para>
/// <para>The file is opened read-only and immutable: TinyMUX keeps the database in WAL mode, and what
/// has not reached the main file is in <c>netmux.sqlite-wal</c>, which is not read. Stopping the game
/// first writes everything into the main file.</para>
/// </remarks>
public static partial class TinyMuxDatabaseReader
{
	/// <summary>What a SQLite import calls its source in the notes.</summary>
	private const string SqliteSource = "SQLite database";

	/// <summary>The newest schema version this reader was written against; a newer one is read, with a note.</summary>
	public const int KnownSqliteSchemaVersion = 13;

	private static ReadOnlySpan<byte> SqliteMagic => "SQLite format 3\0"u8;

	/// <summary>Whether the file is a SQLite database (it starts <c>SQLite format 3</c> and a NUL).</summary>
	public static bool IsSqlite(string path)
	{
		try
		{
			using var stream = File.OpenRead(path);
			Span<byte> head = stackalloc byte[16];
			return stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length
				&& head.SequenceEqual(SqliteMagic);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	/// <summary>
	/// Reads a TinyMUX SQLite database: the objects, and its mail (<see cref="PennMUSHDatabase.Mail"/>) and
	/// comsys (<see cref="PennMUSHDatabase.Chat"/>, aliases as <c>CHANALIAS`</c> attributes). The game
	/// that cannot be read throws <see cref="FormatException"/>; mail or comsys that cannot be read is that
	/// part's <c>ReadError</c>, as for a flatfile's companion files.
	/// </summary>
	public static Task<PennMUSHDatabase> ReadSqliteFileAsync(string path, CancellationToken cancellationToken = default)
		=> Task.Run(() => ReadSqlite(path, cancellationToken), cancellationToken);

	/// <inheritdoc cref="ReadSqliteFileAsync"/>
	public static PennMUSHDatabase ReadSqlite(string path, CancellationToken cancellationToken = default)
	{
		var description = Path.GetFileName(path);
		using var connection = OpenReadOnly(path);
		try
		{
			connection.Open();
		}
		catch (SqliteException ex)
		{
			throw new FormatException($"{description}: the SQLite database could not be opened ({ex.Message})", ex);
		}

		try
		{
			return ReadSqlite(connection, description, cancellationToken);
		}
		catch (SqliteException ex)
		{
			throw new FormatException($"{description}: the SQLite database could not be read ({ex.Message})", ex);
		}
	}

	private static SqliteConnection OpenReadOnly(string path)
	{
		// immutable: the copy being imported does not change, so SQLite takes no locks and neither reads nor
		// creates the -wal and -shm files beside it.
		var uri = new Uri(Path.GetFullPath(path)).AbsoluteUri + "?immutable=1";
		return new SqliteConnection(new SqliteConnectionStringBuilder
		{
			DataSource = uri,
			Mode = SqliteOpenMode.ReadOnly,
			Pooling = false
		}.ToString());
	}

	private static PennMUSHDatabase ReadSqlite(SqliteConnection connection, string description, CancellationToken cancellationToken)
	{
		var tables = Tables(connection);
		if (!tables.Contains("objects") || !tables.Contains("attributes") || !tables.Contains("attrnames"))
		{
			throw new FormatException($"{description}: a SQLite database, but not TinyMUX's: it has no objects, attributes " +
				"and attrnames tables");
		}

		var metadata = tables.Contains("metadata") ? Metadata(connection) : [];
		var schema = metadata.GetValueOrDefault("schema_version");
		var file = new RawFile(5, 0, SqliteSource)
		{
			Description = $"TinyMUX SQLite database, schema version {schema}",
			RecordPlayers = metadata.TryGetValue("record_players", out var record) ? (int)record : null
		};

		ReadAttributeNames(connection, file);
		var objects = ReadObjects(connection, file, cancellationToken);
		if (objects.Count == 0)
		{
			throw new FormatException($"{description}: the SQLite database holds no game; TinyMUX writes the game into it " +
				"at its first start");
		}

		ReadAttributes(connection, objects, cancellationToken);
		var database = new Mapper(file).Map();
		if (schema > KnownSqliteSchemaVersion)
		{
			database.ImportNotes.Add(new ImportWarning(ImportWarningKind.Objects,
				$"The database's schema is version {schema}, newer than the {KnownSqliteSchemaVersion} this import was written " +
				"against; it was read the same way, so check the result."));
		}

		database.Mail = ReadPart("mail", () => ReadMail(connection, tables));
		var comsys = ReadPart("comsys", () => ReadComsys(connection, tables));
		if (comsys.Error is { } error)
		{
			database.Chat = new PennMUSHChatDatabase { ReadError = error };
		}
		else if (comsys.Value is { } read)
		{
			TinyMuxComsysReader.Apply(database, read, file.Version);
		}

		return database;
	}

	/// <summary>The mail, or a mail model carrying why it could not be read.</summary>
	private static PennMUSHMailDatabase ReadPart(string part, Func<PennMUSHMailDatabase> read)
	{
		try
		{
			return read();
		}
		catch (Exception ex) when (ex is FormatException or SqliteException or InvalidCastException)
		{
			return new PennMUSHMailDatabase { ReadError = $"the {part} could not be read: {ex.Message}" };
		}
	}

	/// <summary>The comsys, null when the database has none, or why it could not be read.</summary>
	private static (TinyMuxComsysReader.Comsys? Value, string? Error) ReadPart(string part, Func<TinyMuxComsysReader.Comsys?> read)
	{
		try
		{
			return (read(), null);
		}
		catch (Exception ex) when (ex is FormatException or SqliteException or InvalidCastException)
		{
			return (null, $"the {part} could not be read: {ex.Message}");
		}
	}

	private static HashSet<string> Tables(SqliteConnection connection)
	{
		var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		using var command = Command(connection, "SELECT name FROM sqlite_master WHERE type = 'table'");
		using var reader = command.ExecuteReader();
		while (reader.Read()) tables.Add(reader.GetString(0));
		return tables;
	}

	private static HashSet<string> Columns(SqliteConnection connection, string table)
	{
		var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		using var command = Command(connection, $"SELECT name FROM pragma_table_info('{table}')");
		using var reader = command.ExecuteReader();
		while (reader.Read()) columns.Add(reader.GetString(0));
		return columns;
	}

	private static Dictionary<string, long> Metadata(SqliteConnection connection)
	{
		var metadata = new Dictionary<string, long>(StringComparer.Ordinal);
		using var command = Command(connection, "SELECT key, value FROM metadata");
		using var reader = command.ExecuteReader();
		while (reader.Read())
		{
			if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;
			if (reader.GetValue(1) is long number) metadata[reader.GetString(0)] = number;
		}

		return metadata;
	}

	private static void ReadAttributeNames(SqliteConnection connection, RawFile file)
	{
		var hasFlags = Columns(connection, "attrnames").Contains("flags");
		using var command = Command(connection, $"SELECT attrnum, name, {(hasFlags ? "flags" : "0")} FROM attrnames");
		using var reader = command.ExecuteReader();
		while (reader.Read())
		{
			var number = reader.GetInt32(0);
			if (number < TinyMuxNames.FirstUserAttribute || reader.IsDBNull(1)) continue;
			file.UserAttributes[number] = (reader.GetString(1).ToUpperInvariant(), reader.GetInt32(2));
		}
	}

	private static Dictionary<int, RawObject> ReadObjects(SqliteConnection connection, RawFile file, CancellationToken cancellationToken)
	{
		var objects = new Dictionary<int, RawObject>();
		using var command = Command(connection, "SELECT dbref, location, contents, exits, next, link, owner, parent, zone, " +
			"pennies, flags1, flags2, flags3, powers1, powers2 FROM objects ORDER BY dbref");
		using var reader = command.ExecuteReader();
		while (reader.Read())
		{
			cancellationToken.ThrowIfCancellationRequested();
			var dbref = reader.GetInt32(0);
			if (dbref < 0)
			{
				throw new FormatException($"object number {dbref} is negative");
			}

			var record = new RawObject
			{
				DBRef = dbref,
				Location = reader.GetInt32(1),
				Contents = reader.GetInt32(2),
				Exits = reader.GetInt32(3),
				Next = reader.GetInt32(4),
				Link = reader.GetInt32(5),
				Owner = reader.GetInt32(6),
				Parent = reader.GetInt32(7),
				Zone = reader.GetInt32(8),
				Pennies = reader.GetInt32(9),
				Flags1 = Word(reader, 10),
				Flags2 = Word(reader, 11),
				Flags3 = Word(reader, 12),
				Powers1 = Word(reader, 13),
				Powers2 = Word(reader, 14)
			};
			objects[dbref] = record;
			file.Objects.Add(record);
		}

		return objects;
	}

	/// <summary>A flag or power word, which TinyMUX binds as a signed 32-bit number.</summary>
	private static uint Word(SqliteDataReader reader, int ordinal) => unchecked((uint)reader.GetInt64(ordinal));

	private static void ReadAttributes(SqliteConnection connection, Dictionary<int, RawObject> objects, CancellationToken cancellationToken)
	{
		// The earliest databases had no owner and flags columns; their values carry the \x01 prefix instead.
		var columns = Columns(connection, "attributes");
		var owner = columns.Contains("owner") ? "owner" : "-1";
		var flags = columns.Contains("flags") ? "flags" : "0";
		using var command = Command(connection, $"SELECT object, attrnum, value, {owner}, {flags} FROM attributes ORDER BY object, attrnum");
		using var reader = command.ExecuteReader();
		while (reader.Read())
		{
			cancellationToken.ThrowIfCancellationRequested();
			var number = reader.GetInt32(1);
			if (number <= 0 || !objects.TryGetValue(reader.GetInt32(0), out var record)) continue;

			var (prefixOwner, prefixFlags, value) = DecodeInfo(Text(reader, 2));
			switch (number)
			{
				case TinyMuxNames.NameAttribute:
					record.Name = value;
					continue;
				case TinyMuxNames.MoneyAttribute:
					if (int.TryParse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var money))
					{
						record.Pennies = money;
					}

					continue;
			}

			var rowOwner = reader.GetInt32(3);
			var rowFlags = reader.GetInt32(4);
			record.Attributes.Add(new RawAttribute(number, value,
				rowOwner >= 0 ? rowOwner : prefixOwner,
				rowFlags != 0 ? rowFlags : prefixFlags));
		}
	}

	private static PennMUSHMailDatabase ReadMail(SqliteConnection connection, HashSet<string> tables)
	{
		var mail = new PennMUSHMailDatabase
		{
			Flags = PennMUSHMailDatabase.SubjectFlag | PennMUSHMailDatabase.AliasesFlag
		};
		if (!tables.Contains("mail_headers") || !tables.Contains("mail_bodies"))
		{
			return mail;
		}

		var bodies = new Dictionary<int, string>();
		using (var command = Command(connection, "SELECT number, message FROM mail_bodies"))
		using (var reader = command.ExecuteReader())
		{
			while (reader.Read()) bodies[reader.GetInt32(0)] = Text(reader, 1);
		}

		using (var command = Command(connection,
			"SELECT to_player, from_player, body_number, time_str, subject, read_flags FROM mail_headers ORDER BY rowid"))
		using (var reader = command.ExecuteReader())
		{
			while (reader.Read())
			{
				var to = reader.GetInt32(0);
				var body = reader.GetInt32(2);
				var text = bodies.TryGetValue(body, out var found)
					? found
					: throw new FormatException($"a message to #{to} names body {body}, which the database does not have");
				mail.Messages.Add(TinyMuxMailReader.Message(to, reader.GetInt32(1), Text(reader, 3), Text(reader, 4), text,
					reader.GetInt32(5)));
			}
		}

		mail.MessageCount = mail.Messages.Count;
		if (tables.Contains("mail_aliases"))
		{
			using var command = Command(connection, "SELECT owner, name, description, members FROM mail_aliases ORDER BY id");
			using var reader = command.ExecuteReader();
			while (reader.Read())
			{
				var members = Text(reader, 3).Split(' ', StringSplitOptions.RemoveEmptyEntries)
					.Select(member => int.TryParse(member, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
						? n
						: throw new FormatException($"mail alias member '{member}' is not a number"))
					.ToList();
				mail.Aliases.Add(TinyMuxMailReader.Alias(reader.GetInt32(0), Text(reader, 1), Text(reader, 2), members, godPlayer: 1));
			}
		}

		return TinyMuxMailReader.WithColour(mail, 5);
	}

	private static TinyMuxComsysReader.Comsys? ReadComsys(SqliteConnection connection, HashSet<string> tables)
	{
		if (!tables.Contains("channels"))
		{
			return null;
		}

		var users = new Dictionary<string, List<PennMUSHChannelUser>>(StringComparer.Ordinal);
		if (tables.Contains("channel_users"))
		{
			using var command = Command(connection,
				"SELECT channel_name, who, is_on, comtitle_status, gag_join_leave, title FROM channel_users ORDER BY channel_name, who");
			using var reader = command.ExecuteReader();
			while (reader.Read())
			{
				var channel = Text(reader, 0);
				var list = users.TryGetValue(channel, out var existing) ? existing : users[channel] = [];
				list.Add(TinyMuxComsysReader.User(reader.GetInt32(1), reader.GetInt32(2) != 0, reader.GetInt32(3) != 0,
					reader.GetInt32(4) != 0, Text(reader, 5)));
			}
		}

		var chat = new PennMUSHChatDatabase();
		using (var command = Command(connection,
			"SELECT name, header, type, charge, charge_who, chan_obj FROM channels ORDER BY name"))
		using (var reader = command.ExecuteReader())
		{
			while (reader.Read())
			{
				var name = Text(reader, 0);
				chat.Channels.Add(TinyMuxComsysReader.Channel(name, Text(reader, 1), type: reader.GetInt32(2),
					charge: reader.GetInt32(3), chargeWho: reader.GetInt32(4), channelObject: reader.GetInt32(5),
					users.GetValueOrDefault(name) ?? []));
			}
		}

		var aliases = new Dictionary<int, List<(string Alias, string Channel)>>();
		if (tables.Contains("player_channels"))
		{
			using var command = Command(connection, "SELECT who, alias, channel_name FROM player_channels ORDER BY who, alias");
			using var reader = command.ExecuteReader();
			while (reader.Read())
			{
				var who = reader.GetInt32(0);
				var list = aliases.TryGetValue(who, out var existing) ? existing : aliases[who] = [];
				list.Add((Text(reader, 1), Text(reader, 2)));
			}
		}

		return new TinyMuxComsysReader.Comsys(chat, aliases);
	}

	private static SqliteCommand Command(SqliteConnection connection, string text)
	{
		var command = connection.CreateCommand();
		command.CommandText = text;
		return command;
	}

	/// <summary>A text or blob column as UTF-8 text, without the terminating NUL a C string may have left on it.</summary>
	private static string Text(SqliteDataReader reader, int ordinal)
	{
		var text = reader.IsDBNull(ordinal)
			? string.Empty
			: reader.GetValue(ordinal) switch
			{
				byte[] bytes => Encoding.UTF8.GetString(bytes),
				string s => s,
				var other => Convert.ToString(other, CultureInfo.InvariantCulture) ?? string.Empty
			};
		return text.TrimEnd('\0');
	}
}
