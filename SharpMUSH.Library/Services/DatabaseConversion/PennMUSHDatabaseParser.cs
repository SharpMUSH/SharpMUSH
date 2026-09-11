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
					// The game's flag, power and attribute tables: its definitions, not its objects.
					await reader.ReadLineAsync(cancellationToken);
					while (await reader.PeekAsync(cancellationToken) is not ('+' or '~' or '!' or '*' or -1))
					{
						await reader.ReadLabeledAsync(cancellationToken);
					}

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
		var locks = new Dictionary<string, string>();
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
						await reader.ReadLabeledAsync("creator", cancellationToken);
						await reader.ReadLabeledAsync("flags", cancellationToken);
						await reader.ReadLabeledAsync("derefs", cancellationToken);
						locks[lockType] = await reader.ReadLabeledAsync("key", cancellationToken);
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

	private static List<string> Words(string value) => [.. value.Split(' ', StringSplitOptions.RemoveEmptyEntries)];
}
