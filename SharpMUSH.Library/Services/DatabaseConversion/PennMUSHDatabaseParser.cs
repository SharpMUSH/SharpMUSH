using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// Parser for PennMUSH database files.
/// Reads the text-based PennMUSH database format and converts it into PennMUSHDatabase objects.
/// </summary>
public partial class PennMUSHDatabaseParser
{
	private readonly ILogger<PennMUSHDatabaseParser> _logger;

	public PennMUSHDatabaseParser(ILogger<PennMUSHDatabaseParser> logger)
	{
		_logger = logger;
	}

	/// <summary>
	/// Parse a PennMUSH database file from a stream
	/// </summary>
	public async Task<PennMUSHDatabase> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
	{
		using var reader = new StreamReader(stream);
		return await ParseAsync(reader, cancellationToken);
	}

	/// <summary>
	/// Parse a PennMUSH database file from a file path
	/// </summary>
	public async Task<PennMUSHDatabase> ParseFileAsync(string filePath, CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("Starting to parse PennMUSH database file: {FilePath}", filePath);

		using var stream = File.OpenRead(filePath);
		return await ParseAsync(stream, cancellationToken);
	}

	private async Task<PennMUSHDatabase> ParseAsync(StreamReader reader, CancellationToken cancellationToken)
	{
		var database = new PennMUSHDatabase
		{
			Version = "Unknown"
		};

		// Read header/version information
		var versionLine = await reader.ReadLineAsync(cancellationToken);
		if (versionLine != null)
		{
			database.Version = versionLine.Trim();
			_logger.LogInformation("Database version: {Version}", database.Version);
		}

		// Parse configuration flags if present
		await ParseDatabaseHeaderAsync(reader, database, cancellationToken);

		// Parse objects
		string? line;
		while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Check if this is the start of an object
			if (line.StartsWith('!'))
			{
				// Reset to before this line to let ParseObjectAsync handle it
				reader.BaseStream.Position -= line.Length + Environment.NewLine.Length;
				reader.DiscardBufferedData();

				var obj = await ParseObjectAsync(reader, cancellationToken);
				if (obj != null)
				{
					database.Objects.Add(obj);
					
					if (database.Objects.Count % 100 == 0)
					{
						_logger.LogDebug("Parsed {Count} objects", database.Objects.Count);
					}
				}
			}
		}

		_logger.LogInformation("Completed parsing PennMUSH database: {Count} objects", database.Objects.Count);
		return database;
	}

	private async Task ParseDatabaseHeaderAsync(StreamReader reader, PennMUSHDatabase database, CancellationToken cancellationToken)
	{
		// PennMUSH databases may have configuration flags at the start
		// Format varies by version, but typically includes flags like:
		// +FLAGS, +POWERS, etc.
		
		var position = reader.BaseStream.Position;
		var line = await reader.ReadLineAsync(cancellationToken);

		while (line != null && (line.StartsWith('+') || line.StartsWith('~')))
		{
			var parts = line.Split('|', 2);
			if (parts.Length >= 1)
			{
				var key = parts[0].TrimStart('+', '~');
				var value = parts.Length > 1 ? parts[1] : "";
				database.Configuration[key] = value;
			}

			position = reader.BaseStream.Position;
			line = await reader.ReadLineAsync(cancellationToken);
		}

		// Reset to before the line that wasn't a header
		if (line != null && !line.StartsWith('+') && !line.StartsWith('~'))
		{
			reader.BaseStream.Position = position;
			reader.DiscardBufferedData();
		}
	}

	private async Task<PennMUSHObject?> ParseObjectAsync(StreamReader reader, CancellationToken cancellationToken)
	{
		// PennMUSH object format starts with !<number>
		var line = await reader.ReadLineAsync(cancellationToken);
		
		if (string.IsNullOrWhiteSpace(line))
		{
			return null;
		}

		// Check for object marker
		if (!line.StartsWith('!'))
		{
			_logger.LogWarning("Expected object marker, got: {Line}", line);
			return null;
		}

		var dbrefStr = line.TrimStart('!');
		if (!int.TryParse(dbrefStr, out var dbref))
		{
			_logger.LogWarning("Invalid DBRef: {Line}", line);
			return null;
		}

		// Read object fields
		var name = (await reader.ReadLineAsync(cancellationToken))?.Trim() ?? "";
		var location = ParseDbRef(await reader.ReadLineAsync(cancellationToken));
		var contents = ParseDbRef(await reader.ReadLineAsync(cancellationToken));
		var exits = ParseDbRef(await reader.ReadLineAsync(cancellationToken));
		var link = ParseDbRef(await reader.ReadLineAsync(cancellationToken));
		var next = ParseDbRef(await reader.ReadLineAsync(cancellationToken));
		
		// Locks
		var lockLine = (await reader.ReadLineAsync(cancellationToken))?.Trim() ?? "";
		var locks = ParseLocks(lockLine);

		var owner = ParseDbRef(await reader.ReadLineAsync(cancellationToken));
		var parent = ParseDbRef(await reader.ReadLineAsync(cancellationToken));
		var pennies = ParseInt(await reader.ReadLineAsync(cancellationToken));
		
		// Flags and type
		var flagsLine = (await reader.ReadLineAsync(cancellationToken))?.Trim() ?? "";
		var (type, flags) = ParseFlagsAndType(flagsLine);

		// Powers
		var powersLine = (await reader.ReadLineAsync(cancellationToken))?.Trim() ?? "";
		var powers = ParsePowers(powersLine);

		// Warnings (if present in newer versions)
		var warningsLine = (await reader.ReadLineAsync(cancellationToken))?.Trim() ?? "";
		var warnings = ParseWarnings(warningsLine);

		// Timestamps
		var creationTime = ParseLong(await reader.ReadLineAsync(cancellationToken));
		var modificationTime = ParseLong(await reader.ReadLineAsync(cancellationToken));

		// Attributes
		var attributes = new List<PennMUSHAttribute>();
		await ParseAttributesAsync(reader, attributes, cancellationToken);

		// Zone (may be at end)
		var zone = -1;

		var obj = new PennMUSHObject
		{
			DBRef = dbref,
			Name = name,
			Location = location,
			Contents = contents,
			Exits = exits,
			Link = link,
			Next = next,
			Owner = owner,
			Parent = parent,
			Zone = zone,
			Pennies = pennies,
			Type = type,
			Flags = flags,
			Powers = powers,
			Warnings = warnings,
			CreationTime = creationTime,
			ModificationTime = modificationTime,
			Attributes = attributes,
			Locks = locks
		};

		_logger.LogDebug("Parsed object #{DBRef}: {Name} ({Type})", dbref, name, type);
		return obj;
	}

	private async Task ParseAttributesAsync(StreamReader reader, List<PennMUSHAttribute> attributes, CancellationToken cancellationToken)
	{
		// Attributes start with < marker
		// Format: <name>^owner^flags^derefs
		// Value follows, terminated by either:
		// - Another < (next attribute)
		// - !<number> (next object)
		// - End of stream

		string? line;
		while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			// Check if this is the start of next object or end of attributes
			if (line.StartsWith('!'))
			{
				// Reset position to before this line
				var position = reader.BaseStream.Position;
				reader.BaseStream.Position = position - line.Length - Environment.NewLine.Length;
				reader.DiscardBufferedData();
				break;
			}

			// Check if this is an attribute header
			if (line.StartsWith('<'))
			{
				var attr = await ParseAttributeAsync(reader, line, cancellationToken);
				if (attr != null)
				{
					attributes.Add(attr);
				}
			}
			else
			{
				// Not an attribute marker, this might be part of previous attribute value
				// Reset and break
				var position = reader.BaseStream.Position;
				reader.BaseStream.Position = position - line.Length - Environment.NewLine.Length;
				reader.DiscardBufferedData();
				break;
			}
		}
	}

	private async Task<PennMUSHAttribute?> ParseAttributeAsync(StreamReader reader, string headerLine, CancellationToken cancellationToken)
	{
		// Parse attribute header: <name>^owner^flags^derefs
		var header = headerLine.TrimStart('<').TrimEnd('>');
		var parts = header.Split('^');

		if (parts.Length < 1)
		{
			return null;
		}

		var name = parts[0];
		var owner = parts.Length > 1 ? ParseInt(parts[1]) : (int?)null;
		var flagsStr = parts.Length > 2 ? parts[2] : "";
		var derefCount = parts.Length > 3 ? ParseInt(parts[3]) : 0;

		var flags = flagsStr.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

		// Read attribute value (can be multi-line)
		var value = await ReadAttributeValueAsync(reader, cancellationToken);

		return new PennMUSHAttribute
		{
			Name = name,
			Owner = owner == -1 ? null : owner,
			Flags = flags,
			DerefCount = derefCount,
			Value = value
		};
	}

	private async Task<string> ReadAttributeValueAsync(StreamReader reader, CancellationToken cancellationToken)
	{
		var valueLines = new List<string>();
		long position = reader.BaseStream.Position;

		string? line;
		while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
		{
			// Check if this is the start of next attribute or object
			if (line.StartsWith('<') || line.StartsWith('!'))
			{
				// Reset position to before this line
				reader.BaseStream.Position = position;
				reader.DiscardBufferedData();
				break;
			}

			valueLines.Add(line);
			position = reader.BaseStream.Position;
		}

		return string.Join('\n', valueLines);
	}

	private static int ParseDbRef(string? line)
	{
		if (string.IsNullOrWhiteSpace(line))
		{
			return -1;
		}

		var trimmed = line.Trim().TrimStart('#');
		return int.TryParse(trimmed, out var result) ? result : -1;
	}

	private static int ParseInt(string? line)
	{
		if (string.IsNullOrWhiteSpace(line))
		{
			return 0;
		}

		return int.TryParse(line.Trim(), out var result) ? result : 0;
	}

	private static long ParseLong(string? line)
	{
		if (string.IsNullOrWhiteSpace(line))
		{
			return 0;
		}

		return long.TryParse(line.Trim(), out var result) ? result : 0;
	}

	private static Dictionary<string, string> ParseLocks(string line)
	{
		var locks = new Dictionary<string, string>();
		
		if (string.IsNullOrWhiteSpace(line))
		{
			return locks;
		}

		// Basic lock format: key:value|key:value
		var lockParts = line.Split('|');
		foreach (var part in lockParts)
		{
			var keyValue = part.Split(':', 2);
			if (keyValue.Length == 2)
			{
				locks[keyValue[0]] = keyValue[1];
			}
		}

		return locks;
	}

	private static (PennMUSHObjectType type, List<string> flags) ParseFlagsAndType(string line)
	{
		// Flags format typically includes type indicator
		var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var type = PennMUSHObjectType.Thing; // default
		var flags = new List<string>();

		foreach (var part in parts)
		{
			switch (part.ToUpperInvariant())
			{
				case "ROOM":
				case "TYPE_ROOM":
					type = PennMUSHObjectType.Room;
					break;
				case "PLAYER":
				case "TYPE_PLAYER":
					type = PennMUSHObjectType.Player;
					break;
				case "EXIT":
				case "TYPE_EXIT":
					type = PennMUSHObjectType.Exit;
					break;
				case "THING":
				case "TYPE_THING":
					type = PennMUSHObjectType.Thing;
					break;
				default:
					flags.Add(part);
					break;
			}
		}

		return (type, flags);
	}

	private static List<string> ParsePowers(string line)
	{
		if (string.IsNullOrWhiteSpace(line))
		{
			return [];
		}

		return line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
	}

	private static List<string> ParseWarnings(string line)
	{
		if (string.IsNullOrWhiteSpace(line))
		{
			return [];
		}

		return line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
	}
}
