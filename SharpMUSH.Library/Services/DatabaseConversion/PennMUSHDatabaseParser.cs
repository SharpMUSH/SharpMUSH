using System.Globalization;
using Microsoft.Extensions.Logging;

namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// Reads a PennMUSH database dump — the labeled format PennMUSH 1.8 writes (<c>db_write</c> in
/// src/db.c) — into a <see cref="PennMUSHDatabase"/>.
/// </summary>
/// <remarks>
/// <para>A dump is a <c>+V</c> header line, <c>dbversion</c> and <c>savedtime</c>, the game's flag,
/// power and attribute tables (each a <c>+</c> section), <c>~</c> and the object count, one
/// <c>!N</c> record per object, and <c>***END OF DUMP***</c>. An object record is labeled fields:
/// <c>name</c>, <c>location</c>, <c>contents</c>, <c>exits</c>, <c>next</c>, <c>parent</c>, its locks
/// (<c>lockcount</c> then <c>type</c>/<c>creator</c>/<c>flags</c>/<c>derefs</c>/<c>key</c> each),
/// <c>owner</c>, <c>zone</c>, <c>pennies</c>, <c>type</c>, <c>flags</c>, <c>powers</c>,
/// <c>warnings</c>, <c>created</c>, <c>modified</c>, and its attributes (<c>attrcount</c> then
/// <c>name</c>/<c>owner</c>/<c>flags</c>/<c>derefs</c>/<c>value</c> each).</para>
/// <para>A pre-label dump from an older PennMUSH is refused. PennMUSH itself still reads those, and
/// dumps them back out labeled, so loading one into a current PennMUSH is the way across.</para>
/// </remarks>
public class PennMUSHDatabaseParser(ILogger<PennMUSHDatabaseParser> logger)
{
	private const int Nothing = -1;

	/// <summary>PennMUSH's type bits (<c>TYPE_ROOM</c> and so on in hdrs/flags.h).</summary>
	private const int TypeRoom = 0x1, TypeThing = 0x2, TypeExit = 0x4, TypePlayer = 0x8, TypeGarbage = 0x10;

	private const string EndOfDump = "***END OF DUMP***";

	public async Task<PennMUSHDatabase> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
	{
		using var reader = new StreamReader(stream);
		return await ParseAsync(new PennMUSHDumpReader(reader), cancellationToken);
	}

	public async Task<PennMUSHDatabase> ParseFileAsync(string filePath, CancellationToken cancellationToken = default)
	{
		logger.LogInformation("Starting to parse PennMUSH database file: {FilePath}", filePath);

		await using var stream = File.OpenRead(filePath);
		return await ParseAsync(stream, cancellationToken);
	}

	public async Task<PennMUSHMailDatabase> ParseMailAsync(Stream stream, CancellationToken cancellationToken = default)
	{
		using var reader = new StreamReader(stream);
		return await ParseMailAsync(new PennMUSHDumpReader(reader), cancellationToken);
	}

	public async Task<PennMUSHMailDatabase> ParseMailFileAsync(string filePath, CancellationToken cancellationToken = default)
	{
		logger.LogInformation("Starting to parse PennMUSH mail database file: {FilePath}", filePath);

		await using var stream = File.OpenRead(filePath);
		return await ParseMailAsync(stream, cancellationToken);
	}

	/// <summary>
	/// <c>load_mail</c>'s flags line and <c>load_malias</c>'s section: a count, then per alias its owner,
	/// name, description, use and see bits, member count and members, then <c>"*** End of MALIAS ***"</c>.
	/// Of the messages that follow only their count is read.
	/// </summary>
	private async Task<PennMUSHMailDatabase> ParseMailAsync(PennMUSHDumpReader reader, CancellationToken cancellationToken)
	{
		var mail = new PennMUSHMailDatabase();
		if (await reader.PeekAsync(cancellationToken) != '+')
		{
			mail.MessageCount = await ReadMessageCountAsync(reader, cancellationToken);
			return mail;
		}

		var flagsLine = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
		mail.Flags = int.TryParse(flagsLine.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags)
			? flags
			: throw reader.Error($"expected the mail flags, found '{flagsLine}'");

		if ((mail.Flags & PennMUSHMailDatabase.AliasesFlag) == 0)
		{
			mail.MessageCount = await ReadMessageCountAsync(reader, cancellationToken);
			return mail;
		}

		var count = await reader.ReadIntegerAsync(cancellationToken);
		for (var i = 0; i < count; i++)
		{
			var owner = await reader.ReadIntegerAsync(cancellationToken);
			var name = await reader.ReadStringAsync(cancellationToken);
			var description = await reader.ReadStringAsync(cancellationToken);
			var usePrivileges = await reader.ReadIntegerAsync(cancellationToken);
			var seePrivileges = await reader.ReadIntegerAsync(cancellationToken);
			var size = await reader.ReadIntegerAsync(cancellationToken);

			// Not sized from the count: a corrupt one would be an allocation, not a FormatException.
			var members = new List<int>();
			for (var j = 0; j < size; j++)
			{
				members.Add(await reader.ReadIntegerAsync(cancellationToken));
			}

			mail.Aliases.Add(new PennMUSHMailAlias(owner, name, description, usePrivileges, seePrivileges, members));
		}

		var end = await reader.ReadStringAsync(cancellationToken);
		if (end != "*** End of MALIAS ***")
		{
			throw reader.Error($"expected the end of the mail aliases, found '{end}'");
		}

		mail.MessageCount = await ReadMessageCountAsync(reader, cancellationToken);

		logger.LogInformation("PennMUSH mail database flags {Flags}, {Count} mail alias(es), {Messages} message(s)",
			mail.Flags, mail.Aliases.Count, mail.MessageCount);
		return mail;
	}

	public async Task<PennMUSHChatDatabase> ParseChatAsync(Stream stream, CancellationToken cancellationToken = default)
	{
		using var reader = new StreamReader(stream);
		return await ParseChatAsync(new PennMUSHDumpReader(reader), cancellationToken);
	}

	public async Task<PennMUSHChatDatabase> ParseChatFileAsync(string filePath, CancellationToken cancellationToken = default)
	{
		logger.LogInformation("Starting to parse PennMUSH chat database file: {FilePath}", filePath);

		await using var stream = File.OpenRead(filePath);
		return await ParseChatAsync(stream, cancellationToken);
	}

	/// <summary>
	/// <c>load_chatdb</c>: a <c>+V</c> flags line, <c>savedtime</c>, <c>channels</c>, then per channel
	/// <c>load_labeled_channel</c>'s fields (<c>buffer</c> and <c>mogrifier</c> only under
	/// <c>CDB_SPIFFY</c>), its <c>lock</c>/<c>key</c> pairs, <c>users</c>, and per user <c>dbref</c>,
	/// <c>flags</c> and <c>title</c>; then <c>***END OF DUMP***</c>.
	/// </summary>
	/// <remarks>
	/// Members are read as written. What <c>load_labeled_chanusers</c> drops or changes on load is the
	/// converter's to decide, since it needs the imported objects to decide it.
	/// </remarks>
	private async Task<PennMUSHChatDatabase> ParseChatAsync(PennMUSHDumpReader reader, CancellationToken cancellationToken)
	{
		var header = await reader.ReadLineAsync(cancellationToken);
		if (header is null || !header.StartsWith("+V", StringComparison.Ordinal))
		{
			throw new FormatException(
				"Not a PennMUSH chat database in the labeled format PennMUSH 1.8 writes, which begins with a '+V' line. " +
				"An older chatdb loads into a current PennMUSH, which writes it back out labeled.");
		}

		var chat = new PennMUSHChatDatabase
		{
			Flags = int.TryParse(header.AsSpan(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags)
				? flags
				: throw reader.Error($"expected the chat database flags, found '{header}'"),
			SavedTime = await reader.ReadLabeledAsync("savedtime", cancellationToken)
		};

		var count = Int(reader, await reader.ReadLabeledAsync("channels", cancellationToken));
		for (var i = 0; i < count; i++)
		{
			chat.Channels.Add(await ParseChannelAsync(reader, chat.Flags, cancellationToken));
		}

		var end = await reader.ReadLineAsync(cancellationToken);
		while (end is not null && string.IsNullOrWhiteSpace(end))
		{
			end = await reader.ReadLineAsync(cancellationToken);
		}

		if (end != EndOfDump)
		{
			throw reader.Error($"expected '{EndOfDump}' after {count} channel(s), found '{end}'");
		}

		logger.LogInformation("PennMUSH chat database flags {Flags}, {Count} channel(s)", chat.Flags, chat.Channels.Count);
		return chat;
	}

	private static async Task<PennMUSHChannel> ParseChannelAsync(PennMUSHDumpReader reader, int chatFlags,
		CancellationToken cancellationToken)
	{
		var name = await reader.ReadLabeledAsync("name", cancellationToken);
		var description = await reader.ReadLabeledAsync("description", cancellationToken);
		var flags = Int(reader, await reader.ReadLabeledAsync("flags", cancellationToken));
		var creator = DbRef(reader, await reader.ReadLabeledAsync("creator", cancellationToken));
		var cost = Int(reader, await reader.ReadLabeledAsync("cost", cancellationToken));
		var buffer = 0;
		var mogrifier = Nothing;
		if ((chatFlags & PennMUSHChatDatabase.SpiffyFlag) != 0)
		{
			buffer = Int(reader, await reader.ReadLabeledAsync("buffer", cancellationToken));
			mogrifier = DbRef(reader, await reader.ReadLabeledAsync("mogrifier", cancellationToken));
		}

		var locks = new Dictionary<string, string>(StringComparer.Ordinal);
		var (label, value) = await reader.ReadLabeledAsync(cancellationToken);
		while (label == "lock")
		{
			locks[value] = await reader.ReadLabeledAsync("key", cancellationToken);
			(label, value) = await reader.ReadLabeledAsync(cancellationToken);
		}

		if (label != "users")
		{
			throw reader.Error($"expected 'users' for channel '{name}', found '{label}'");
		}

		var userCount = Int(reader, value);
		var users = new List<PennMUSHChannelUser>();
		for (var i = 0; i < userCount; i++)
		{
			var dbref = DbRef(reader, await reader.ReadLabeledAsync("dbref", cancellationToken));
			var userFlags = Int(reader, await reader.ReadLabeledAsync("flags", cancellationToken));
			var title = await reader.ReadLabeledAsync("title", cancellationToken);
			users.Add(new PennMUSHChannelUser(dbref, userFlags, title));
		}

		return new PennMUSHChannel(name, description, flags, creator, cost, buffer, mogrifier, locks, users);
	}

	/// <summary><c>load_mail</c>'s message count line; <c>null</c> when it is missing or not a number.</summary>
	private static async ValueTask<int?> ReadMessageCountAsync(PennMUSHDumpReader reader, CancellationToken cancellationToken)
		=> int.TryParse(await reader.ReadLineAsync(cancellationToken), NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var count)
			? count
			: null;

	private async Task<PennMUSHDatabase> ParseAsync(PennMUSHDumpReader reader, CancellationToken cancellationToken)
	{
		var header = await reader.ReadLineAsync(cancellationToken);
		if (header is null || !header.StartsWith("+V", StringComparison.Ordinal))
		{
			throw new FormatException(
				"Not a PennMUSH database in the labeled format PennMUSH 1.8 writes, which begins with a '+V' line. " +
				"A dump from an older PennMUSH loads into a current one, which writes it back out labeled.");
		}

		var database = new PennMUSHDatabase { Version = header };

		var (label, value) = await reader.ReadLabeledAsync(cancellationToken);
		if (label == "dbversion")
		{
			database.Configuration["dbversion"] = value;
			(label, value) = await reader.ReadLabeledAsync(cancellationToken);
		}

		if (label != "savedtime")
		{
			throw reader.Error($"expected 'savedtime', found '{label}'");
		}

		database.Configuration["savedtime"] = value;
		logger.LogInformation("PennMUSH database {Header}, dbversion {Version}, saved {SavedTime}",
			header, database.Configuration.GetValueOrDefault("dbversion", "?"), value);

		while (await reader.PeekAsync(cancellationToken) is var next and not -1)
		{
			switch (next)
			{
				case '+':
					await ReadDefinitionTableAsync(reader, database, cancellationToken);
					break;

				case '~':
					database.Configuration["objects"] = (await reader.ReadLineAsync(cancellationToken))![1..];
					break;

				case '!':
					if (await ReadObjectAsync(reader, cancellationToken) is { } obj)
					{
						database.Objects.Add(obj);
					}

					break;

				case '*':
					if (await reader.ReadLineAsync(cancellationToken) != EndOfDump)
					{
						throw reader.Error($"expected '{EndOfDump}'");
					}

					logger.LogInformation("Completed parsing PennMUSH database: {Count} objects", database.Objects.Count);
					return database;

				case '\r' or '\n':
					await reader.ReadLineAsync(cancellationToken);
					break;

				default:
					throw reader.Error($"unexpected '{(char)next}' at the start of a line");
			}
		}

		throw reader.Error($"the database ends without '{EndOfDump}', so it is incomplete");
	}

	/// <summary>
	/// One of the dump's definition tables: the game's own flags, powers and standard attributes, each
	/// a count, that many entries, and a second count with that many alias rows (<c>flag_write_all</c>
	/// in src/flags.c, <c>attr_write_all</c> in src/attrib.c).
	/// </summary>
	private async Task ReadDefinitionTableAsync(PennMUSHDumpReader reader, PennMUSHDatabase database,
		CancellationToken cancellationToken)
	{
		var section = (await reader.ReadLineAsync(cancellationToken))!;

		// A table is read whole into a list of its own and handed over only once it is complete, so a
		// table SharpMUSH cannot make sense of costs the game that table and nothing else: not the
		// objects, which are the point of an import, and not the part of the table read before the
		// trouble, which would otherwise stand in for the game's whole definition set. Both the unknown
		// section and the unreadable one leave through the skip below, which resynchronises on the next
		// section — a definition table is always followed by one.
		try
		{
			switch (section)
			{
				case "+FLAGS LIST":
					database.FlagDefinitions.AddRange(await ReadFlagTableAsync(reader, cancellationToken));
					return;

				case "+POWER LIST":
					// PennMUSH keeps its powers in a flag table of their own, written by the same routine.
					database.PowerDefinitions.AddRange(await ReadFlagTableAsync(reader, cancellationToken));
					return;

				case "+ATTRIBUTES LIST":
					database.AttributeDefinitions.AddRange(await ReadAttributeTableAsync(reader, cancellationToken));
					return;

				default:
					logger.LogWarning("Skipping PennMUSH database section {Section}, which SharpMUSH does not read", section);
					break;
			}
		}
		catch (FormatException ex)
		{
			logger.LogWarning(ex,
				"Reading the PennMUSH {Section} section failed; its definitions are dropped and the world still loads", section);
		}

		await SkipLabeledAsync(reader, cancellationToken);
	}

	/// <summary>
	/// A flag or power table's own rows: <c>flagcount</c> entries of name, letter, types and the two
	/// permission sets, then <c>flagaliascount</c> alias rows. An unknown top-level label is ignored,
	/// so a future count SharpMUSH does not know costs the table nothing.
	/// </summary>
	private async Task<List<PennMUSHFlagDefinition>> ReadFlagTableAsync(PennMUSHDumpReader reader,
		CancellationToken cancellationToken)
	{
		var definitions = new List<PennMUSHFlagDefinition>();
		while (await reader.PeekAsync(cancellationToken) is not ('+' or '~' or '!' or '*' or -1))
		{
			var (label, value) = await reader.ReadLabeledAsync(cancellationToken);
			switch (label)
			{
				case "flagcount":
					for (var i = Int(reader, value); i > 0; i--)
					{
						definitions.Add(new PennMUSHFlagDefinition
						{
							Name = await reader.ReadLabeledAsync("name", cancellationToken),
							Letter = await reader.ReadLabeledAsync("letter", cancellationToken),
							Types = Words(await reader.ReadLabeledAsync("type", cancellationToken)),
							SetPermissions = Words(await reader.ReadLabeledAsync("perms", cancellationToken)),
							UnsetPermissions = Words(await reader.ReadLabeledAsync("negate_perms", cancellationToken))
						});
					}

					break;

				case "flagaliascount":
					await ReadAliasRowsAsync(reader, Int(reader, value),
						name => definitions.Find(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Aliases,
						cancellationToken);
					break;

				default:
					logger.LogDebug("Ignoring field {Label} of a PennMUSH flag table", label);
					break;
			}
		}

		return definitions;
	}

	/// <summary>
	/// The standard-attribute table's own rows: <c>attrcount</c> entries of name, default flags,
	/// creator and default value, then <c>attraliascount</c> alias rows.
	/// </summary>
	private async Task<List<PennMUSHAttributeDefinition>> ReadAttributeTableAsync(PennMUSHDumpReader reader,
		CancellationToken cancellationToken)
	{
		var definitions = new List<PennMUSHAttributeDefinition>();
		while (await reader.PeekAsync(cancellationToken) is not ('+' or '~' or '!' or '*' or -1))
		{
			var (label, value) = await reader.ReadLabeledAsync(cancellationToken);
			switch (label)
			{
				case "attrcount":
					for (var i = Int(reader, value); i > 0; i--)
					{
						definitions.Add(new PennMUSHAttributeDefinition
						{
							Name = await reader.ReadLabeledAsync("name", cancellationToken),
							Flags = Words(await reader.ReadLabeledAsync("flags", cancellationToken)),
							Creator = OptionalDbRef(reader, await reader.ReadLabeledAsync("creator", cancellationToken)),
							Data = await reader.ReadLabeledAsync("data", cancellationToken)
						});
					}

					break;

				case "attraliascount":
					await ReadAliasRowsAsync(reader, Int(reader, value),
						name => definitions.Find(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Aliases,
						cancellationToken);
					break;

				default:
					logger.LogDebug("Ignoring field {Label} of a PennMUSH attribute table", label);
					break;
			}
		}

		return definitions;
	}

	/// <summary>
	/// A table's alias rows, each a definition's name and one alias, onto the definition they name.
	/// An alias for a name the table never declared is dropped rather than guessed at.
	/// </summary>
	private async Task ReadAliasRowsAsync(PennMUSHDumpReader reader, int count,
		Func<string, List<string>?> aliasesOf, CancellationToken cancellationToken)
	{
		for (; count > 0; count--)
		{
			var name = await reader.ReadLabeledAsync("name", cancellationToken);
			var alias = await reader.ReadLabeledAsync("alias", cancellationToken);
			if (aliasesOf(name) is { } aliases)
			{
				aliases.Add(alias);
			}
			else
			{
				logger.LogWarning("Dropping alias {Alias}, which names {Name}, undefined in its table", alias, name);
			}
		}
	}

	private static async Task SkipLabeledAsync(PennMUSHDumpReader reader, CancellationToken cancellationToken)
	{
		while (await reader.PeekAsync(cancellationToken) is not ('+' or '~' or '!' or '*' or -1))
		{
			await reader.ReadLabeledAsync(cancellationToken);
		}
	}

	private async Task<PennMUSHObject?> ReadObjectAsync(PennMUSHDumpReader reader, CancellationToken cancellationToken)
	{
		var marker = (await reader.ReadLineAsync(cancellationToken))!;
		if (!int.TryParse(marker.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var dbref))
		{
			throw reader.Error($"'{marker}' is not an object marker");
		}

		string? name = null;
		int? type = null;
		int location = Nothing, contents = Nothing, exits = Nothing, next = Nothing, parent = Nothing;
		int owner = Nothing, zone = Nothing, pennies = 0;
		long created = 0, modified = 0;
		List<string> flags = [], powers = [], warnings = [];
		var locks = new Dictionary<string, PennMUSHLock>();
		var attributes = new List<PennMUSHAttribute>();

		while (await reader.PeekAsync(cancellationToken) is not ('!' or '*' or '~' or '+' or -1))
		{
			var (label, value) = await reader.ReadLabeledAsync(cancellationToken);
			switch (label)
			{
				case "name": name = value; break;
				case "location": location = DbRef(reader, value); break;
				case "contents": contents = DbRef(reader, value); break;
				case "exits": exits = DbRef(reader, value); break;
				case "next": next = DbRef(reader, value); break;
				case "parent": parent = DbRef(reader, value); break;
				case "owner": owner = DbRef(reader, value); break;
				case "zone": zone = DbRef(reader, value); break;
				case "pennies": pennies = Int(reader, value); break;
				case "type": type = Int(reader, value); break;
				case "flags": flags = Words(value); break;
				case "powers": powers = Words(value); break;
				case "warnings": warnings = Words(value); break;
				case "created": created = Int(reader, value); break;
				case "modified": modified = Int(reader, value); break;

				case "lockcount":
					for (var i = Int(reader, value); i > 0; i--)
					{
						var lockType = await reader.ReadLabeledAsync("type", cancellationToken);
						var creator = OptionalDbRef(reader, await reader.ReadLabeledAsync("creator", cancellationToken));
						var lockFlags = ParseLockFlags(await reader.ReadLabeledAsync("flags", cancellationToken));
						await reader.ReadLabeledAsync("derefs", cancellationToken);
						locks[lockType] = new PennMUSHLock(await reader.ReadLabeledAsync("key", cancellationToken), lockFlags, creator);
					}

					break;

				case "attrcount":
					for (var i = Int(reader, value); i > 0; i--)
					{
						attributes.Add(new PennMUSHAttribute
						{
							Name = await reader.ReadLabeledAsync("name", cancellationToken),
							Owner = OptionalDbRef(reader, await reader.ReadLabeledAsync("owner", cancellationToken)),
							Flags = Words(await reader.ReadLabeledAsync("flags", cancellationToken)),
							DerefCount = Int(reader, await reader.ReadLabeledAsync("derefs", cancellationToken)),
							Value = await reader.ReadLabeledAsync("value", cancellationToken)
						});
					}

					break;

				default:
					logger.LogDebug("Ignoring field {Label} of #{DBRef}", label, dbref);
					break;
			}
		}

		if (name is null || type is null)
		{
			throw reader.Error($"#{dbref} has no {(name is null ? "name" : "type")}");
		}

		var objectType = type switch
		{
			TypeRoom => PennMUSHObjectType.Room,
			TypeThing => PennMUSHObjectType.Thing,
			TypeExit => PennMUSHObjectType.Exit,
			TypePlayer => PennMUSHObjectType.Player,
			TypeGarbage => (PennMUSHObjectType?)null,
			_ => throw reader.Error($"#{dbref} has unknown type {type}")
		};
		if (objectType is null)
		{
			return null;
		}

		// PennMUSH overloads two fields by type (hdrs/dbdefs.h): a room's location is its drop-to; an
		// exit's location is its destination and its exits its source; a thing's or player's exits is
		// its home.
		var (placedIn, link) = objectType switch
		{
			PennMUSHObjectType.Room => (Nothing, location),
			PennMUSHObjectType.Exit => (exits, location),
			_ => (location, exits)
		};

		// A player's password is its XYXXY attribute. It is lifted out rather than imported, which
		// would leave the hash readable as an ordinary attribute.
		var password = attributes.Find(a => a.Name.Equals("XYXXY", StringComparison.OrdinalIgnoreCase))?.Value;
		attributes.RemoveAll(a => a.Name.Equals("XYXXY", StringComparison.OrdinalIgnoreCase));

		var aliases = attributes.Find(a => a.Name.Equals("ALIAS", StringComparison.OrdinalIgnoreCase))?.Value
			.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

		return new PennMUSHObject
		{
			DBRef = dbref,
			Name = name,
			Type = objectType.Value,
			Location = placedIn,
			Contents = contents,
			Exits = objectType == PennMUSHObjectType.Room ? exits : Nothing,
			Link = link,
			Next = next,
			Owner = owner,
			Parent = parent,
			Zone = zone,
			Pennies = pennies,
			Flags = flags,
			Powers = powers,
			Warnings = warnings,
			CreationTime = created,
			ModificationTime = modified,
			Locks = locks,
			Attributes = attributes,
			Password = password,
			Aliases = [.. aliases]
		};
	}

	private static int DbRef(PennMUSHDumpReader reader, string value)
		=> value.StartsWith('#') && int.TryParse(value.AsSpan(1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var dbref)
			? dbref
			: throw reader.Error($"'{value}' is not a dbref");

	private static int? OptionalDbRef(PennMUSHDumpReader reader, string value)
		=> DbRef(reader, value) is var dbref and not Nothing ? dbref : null;

	private static int Int(PennMUSHDumpReader reader, string value)
		=> int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
			? number
			: throw reader.Error($"'{value}' is not a number");

	private static int ParseLockFlags(string value)
	{
		if (int.TryParse(value, out var numeric)) return numeric;
		var flags = 0;
		foreach (var name in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			flags |= name.ToLowerInvariant() switch
			{
				"visual" => 0x01,
				"no_inherit" => 0x02,
				"wizard" => 0x04,
				"locked" => 0x08,
				"no_clone" => 0x10,
				_ => throw new FormatException("Unknown PennMUSH lock flag: " + name)
			};
		return flags;
	}

	private static List<string> Words(string value) => [.. value.Split(' ', StringSplitOptions.RemoveEmptyEntries)];
}
