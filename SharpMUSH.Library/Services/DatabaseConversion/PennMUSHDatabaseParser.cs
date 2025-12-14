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
	private string? _nextLine;

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
		_nextLine = null;
		
		var database = new PennMUSHDatabase
		{
			Version = "Unknown"
		};

		// Read header/version information
		var versionLine = await ReadLineAsync(reader, cancellationToken);
		if (versionLine != null)
		{
			database.Version = versionLine.Trim();
			_logger.LogInformation("Database version: {Version}", database.Version);
		}

		// Parse configuration flags if present
		await ParseDatabaseHeaderAsync(reader, database, cancellationToken);

		// Parse objects
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var line = await PeekLineAsync(reader, cancellationToken);
			
			if (line == null)
			{
				break;
			}

			// Check if this is the start of an object
			if (line.StartsWith('!'))
			{
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
			else
			{
				// Skip unexpected lines
				await ReadLineAsync(reader, cancellationToken);
			}
		}

		_logger.LogInformation("Completed parsing PennMUSH database: {Count} objects", database.Objects.Count);
		return database;
	}

	private async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
	{
		if (_nextLine != null)
		{
			var line = _nextLine;
			_nextLine = null;
			return line;
		}

		return await reader.ReadLineAsync(cancellationToken);
	}

	private async Task<string?> PeekLineAsync(StreamReader reader, CancellationToken cancellationToken)
	{
		if (_nextLine == null)
		{
			_nextLine = await reader.ReadLineAsync(cancellationToken);
		}

		return _nextLine;
	}

	private async Task ParseDatabaseHeaderAsync(StreamReader reader, PennMUSHDatabase database, CancellationToken cancellationToken)
	{
		// PennMUSH databases may have configuration flags at the start
		// Format varies by version, but typically includes flags like:
		// +FLAGS, +POWERS, etc.
		
		var line = await PeekLineAsync(reader, cancellationToken);

		while (line != null && (line.StartsWith('+') || line.StartsWith('~')))
		{
			await ReadLineAsync(reader, cancellationToken);
			
			var parts = line.Split('|', 2);
			if (parts.Length >= 1)
			{
				var key = parts[0].TrimStart('+', '~');
				var value = parts.Length > 1 ? parts[1] : "";
				database.Configuration[key] = value;
			}

			line = await PeekLineAsync(reader, cancellationToken);
		}
	}

	private async Task<PennMUSHObject?> ParseObjectAsync(StreamReader reader, CancellationToken cancellationToken)
	{
		// PennMUSH object format starts with !<number>
		var line = await ReadLineAsync(reader, cancellationToken);
		
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
		var name = (await ReadLineAsync(reader, cancellationToken))?.Trim() ?? "";
		var location = ParseDbRef(await ReadLineAsync(reader, cancellationToken));
		var contents = ParseDbRef(await ReadLineAsync(reader, cancellationToken));
		var exits = ParseDbRef(await ReadLineAsync(reader, cancellationToken));
		var link = ParseDbRef(await ReadLineAsync(reader, cancellationToken));
		var next = ParseDbRef(await ReadLineAsync(reader, cancellationToken));
		
		// Locks
		var lockLine = (await ReadLineAsync(reader, cancellationToken))?.Trim() ?? "";
		var locks = ParseLocks(lockLine);

		var owner = ParseDbRef(await ReadLineAsync(reader, cancellationToken));
		var parent = ParseDbRef(await ReadLineAsync(reader, cancellationToken));
		var pennies = ParseInt(await ReadLineAsync(reader, cancellationToken));
		
		// Flags and type
		var flagsLine = (await ReadLineAsync(reader, cancellationToken))?.Trim() ?? "";
		var (type, flags) = ParseFlagsAndType(flagsLine);

		// Powers
		var powersLine = (await ReadLineAsync(reader, cancellationToken))?.Trim() ?? "";
		var powers = ParsePowers(powersLine);

		// Warnings (if present in newer versions)
		var warningsLine = (await ReadLineAsync(reader, cancellationToken))?.Trim() ?? "";
		var warnings = ParseWarnings(warningsLine);

		// Timestamps
		var creationTime = ParseLong(await ReadLineAsync(reader, cancellationToken));
		var modificationTime = ParseLong(await ReadLineAsync(reader, cancellationToken));

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

		while (true)
		{
			var line = await PeekLineAsync(reader, cancellationToken);
			
			if (line == null)
			{
				break;
			}

			if (string.IsNullOrWhiteSpace(line))
			{
				await ReadLineAsync(reader, cancellationToken);
				continue;
			}

			// Check if this is the start of next object or end of attributes
			if (line.StartsWith('!'))
			{
				break;
			}

			// Check if this is an attribute header
			if (line.StartsWith('<'))
			{
				await ReadLineAsync(reader, cancellationToken);
				var attr = await ParseAttributeAsync(reader, line, cancellationToken);
				if (attr != null)
				{
					attributes.Add(attr);
				}
			}
			else
			{
				// Not an attribute marker
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

		while (true)
		{
			var line = await PeekLineAsync(reader, cancellationToken);

			if (line == null)
			{
				break;
			}

			// Check if this is the start of next attribute or object
			if (line.StartsWith('<') || line.StartsWith('!'))
			{
				break;
			}

			await ReadLineAsync(reader, cancellationToken);
			valueLines.Add(line);
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
