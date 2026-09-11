using SharpMUSH.Library.Services.DatabaseConversion;
using System.Text;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Generates large fake PennMUSH databases for performance testing, in the labeled dump format
/// PennMUSH 1.8 writes (see <see cref="PennMUSHDatabaseParser"/> and
/// TestData/pennmush-1.8.8p0.outdb, a real one).
/// </summary>
public static class PennMUSHDatabaseGenerator
{
	private static readonly string[] CommonFlags = ["SAFE", "DARK", "VISUAL", "TRANSPARENT", "WIZARD", "ROYALTY", "INHERIT", "DEBUG", "GOING", "MONITOR", "MYOPIC", "PUPPET", "CHOWN_OK", "ENTER_OK", "LINK_OK", "OPAQUE", "QUIET", "STICKY", "UNFINDABLE", "LIGHT", "HAVEN", "ABODE", "FLOATING", "TRACK_MONEY", "AUDITORIUM", "ANSI"];

	private static readonly string[] CommonPowers = ["Login", "Guest", "See_All", "Cemit", "Can_Boot", "Announce", "Halt", "Pemit_All", "Hide", "See_Queue", "Search", "No_Pay", "No_Quota", "Long_Fingers", "Unkillable", "Give"];

	private static readonly string[] CommonLockTypes = ["Basic", "Enter", "Use", "Leave", "Drop", "Give", "Page", "Mail", "Teleport", "Speech", "Listen", "Command", "Parent", "Zone"];

	private static readonly string[] AttributeNames = ["DESCRIBE", "DESCRIPTION", "SEX", "RACE", "CLASS", "LEVEL", "HP", "MANA", "STR", "DEX", "CON", "INT", "WIS", "CHA", "INVENTORY", "EQUIPMENT", "SKILLS", "SPELLS", "COMBAT", "STATS", "NOTES", "HISTORY", "TITLE", "FULLNAME", "ALIAS", "COLOR", "PROFILE", "STATUS", "MOOD", "QUOTE"];

	private static readonly string[] AttributeFlags = ["no_command", "visual", "regexp", "case", "locked", "mortal_dark", "hidden", "prefixmatch", "veiled", "debug"];

	private static readonly Random Random = new();

	/// <summary>
	/// Generates a fake PennMUSH database file with the specified target size.
	/// </summary>
	/// <param name="targetSizeBytes">Target file size in bytes (e.g., 10MB = 10 * 1024 * 1024)</param>
	/// <returns>Path to the generated database file</returns>
	public static async Task<string> GenerateLargeDatabaseFileAsync(int targetSizeBytes = 10 * 1024 * 1024)
	{
		var tempFile = Path.Combine(Path.GetTempPath(), $"pennmush_test_{Guid.NewGuid()}.db");
		await using (var writer = new StreamWriter(tempFile, false, new UTF8Encoding(false)))
		{
			await WriteHeaderAsync(writer);

			var dbref = 0;
			while (writer.BaseStream.Length < targetSizeBytes)
			{
				await WriteObjectAsync(writer, dbref, TypeFor(dbref));
				dbref++;

				if (dbref % 10 == 0)
				{
					await writer.FlushAsync();
				}
			}

			await writer.WriteLineAsync("***END OF DUMP***");
		}

		return tempFile;
	}

	/// <summary>
	/// Generates a PennMUSH database with a specific number of objects.
	/// </summary>
	/// <param name="objectCount">Number of objects to generate</param>
	/// <returns>Path to the generated database file</returns>
	public static async Task<string> GenerateDatabaseWithObjectCountAsync(int objectCount)
	{
		var tempFile = Path.Combine(Path.GetTempPath(), $"pennmush_test_{Guid.NewGuid()}.db");
		await using (var writer = new StreamWriter(tempFile, false, new UTF8Encoding(false)))
		{
			await WriteHeaderAsync(writer);
			await writer.WriteLineAsync($"~{objectCount}");

			for (var dbref = 0; dbref < objectCount; dbref++)
			{
				await WriteObjectAsync(writer, dbref, TypeFor(dbref));
			}

			await writer.WriteLineAsync("***END OF DUMP***");
		}

		return tempFile;
	}

	/// <summary>
	/// The header a real dump opens with. The flag, power and attribute tables that follow it in a real
	/// dump describe the game rather than its objects, so the generator leaves them out.
	/// </summary>
	private static async Task WriteHeaderAsync(StreamWriter writer)
	{
		await writer.WriteLineAsync("+V-4199422");
		await writer.WriteLineAsync("dbversion 6");
		await WriteLabeledAsync(writer, "savedtime", DateTimeOffset.UtcNow.ToString("ddd MMM dd HH:mm:ss yyyy"));
	}

	/// <summary>
	/// #0-#2 have the shape PennMUSH's <c>create_minimal_db</c> gives every database — Room Zero, God and
	/// the Master Room — and everything after them is random.
	/// </summary>
	private static PennMUSHObjectType TypeFor(int dbref) => dbref switch
	{
		0 or 2 => PennMUSHObjectType.Room,
		1 => PennMUSHObjectType.Player,
		_ => (PennMUSHObjectType)Random.Next(0, 4)
	};

	private static async Task WriteObjectAsync(StreamWriter writer, int dbref, PennMUSHObjectType type)
	{
		// Somewhere earlier in the database, or nowhere. For an exit that is its source, which PennMUSH
		// keeps in the exits field; for a thing or player it is its location.
		var placedIn = dbref > 0 ? Random.Next(-1, dbref) : -1;
		var (location, exits) = type switch
		{
			PennMUSHObjectType.Room => (-1, -1),
			PennMUSHObjectType.Exit => (-1, placedIn),
			_ => (placedIn, -1)
		};

		await writer.WriteLineAsync($"!{dbref}");
		await WriteLabeledAsync(writer, "name", GenerateObjectName(dbref, type));
		await writer.WriteLineAsync($"location #{location}");
		await writer.WriteLineAsync("contents #-1");
		await writer.WriteLineAsync($"exits #{exits}");
		await writer.WriteLineAsync("next #-1");
		await writer.WriteLineAsync("parent #-1");

		var lockCount = Random.Next(1, 6);
		await writer.WriteLineAsync($"lockcount {lockCount}");
		for (var i = 0; i < lockCount; i++)
		{
			await WriteLockAsync(writer);
		}

		// Every object is God's: in a real database most belong to players, but #1 is the one owner
		// every generated database is sure to have.
		await writer.WriteLineAsync("owner #1");
		await writer.WriteLineAsync("zone #-1");
		await writer.WriteLineAsync($"pennies {Random.Next(0, 10000)}");
		await writer.WriteLineAsync($"type {TypeBits(type)}");
		await WriteLabeledAsync(writer, "flags", RandomWords(CommonFlags, 6));
		await WriteLabeledAsync(writer, "powers", RandomWords(CommonPowers, 4));
		await WriteLabeledAsync(writer, "warnings", "");
		await writer.WriteLineAsync($"created {DateTimeOffset.UtcNow.AddDays(-Random.Next(0, 365)).ToUnixTimeSeconds()}");
		await writer.WriteLineAsync($"modified {DateTimeOffset.UtcNow.AddDays(-Random.Next(0, 30)).ToUnixTimeSeconds()}");

		var attributeCount = Random.Next(10, 101);
		var isPlayer = type == PennMUSHObjectType.Player;
		await writer.WriteLineAsync($"attrcount {attributeCount + (isPlayer ? 1 : 0)}");
		for (var i = 0; i < attributeCount; i++)
		{
			await WriteAttributeAsync(writer);
		}

		if (isPlayer)
		{
			await WriteAttributeAsync(writer, "XYXXY", "no_command no_clone wizard locked internal",
				"2:sha512:ab" + Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + ":" + DateTimeOffset.UtcNow.ToUnixTimeSeconds());
		}
	}

	private static async Task WriteAttributeAsync(StreamWriter writer)
	{
		var name = AttributeNames[Random.Next(AttributeNames.Length)];
		if (Random.Next(0, 10) < 2)
		{
			name += "`" + Random.Next(1, 100);
		}

		var value = GenerateRandomText(Random.Next(50, 501));
		if (Random.Next(0, 10) < 3)
		{
			value = $"\x1b[{Random.Next(30, 38)}m{value}\x1b[0m";
		}

		await WriteAttributeAsync(writer, name, RandomWords(AttributeFlags, 3), value);
	}

	private static async Task WriteAttributeAsync(StreamWriter writer, string name, string flags, string value)
	{
		await WriteLabeledAsync(writer, " name", name);
		await writer.WriteLineAsync("  owner #1");
		await WriteLabeledAsync(writer, "  flags", flags);
		await writer.WriteLineAsync($"  derefs {Random.Next(0, 100)}");
		await WriteLabeledAsync(writer, "  value", value);
	}

	private static async Task WriteLockAsync(StreamWriter writer)
	{
		var key = $"#{Random.Next(0, 100)}";
		if (Random.Next(0, 10) < 4)
		{
			key += $"|#{Random.Next(0, 100)}";
		}

		await WriteLabeledAsync(writer, " type", CommonLockTypes[Random.Next(CommonLockTypes.Length)]);
		await writer.WriteLineAsync("  creator #1");
		await WriteLabeledAsync(writer, "  flags", "");
		await writer.WriteLineAsync("  derefs 0");
		await WriteLabeledAsync(writer, "  key", key);
	}

	/// <summary>A labeled, quoted value, escaped as PennMUSH's <c>putstring</c> escapes it.</summary>
	private static Task WriteLabeledAsync(StreamWriter writer, string label, string value)
		=> writer.WriteLineAsync($"{label} \"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");

	/// <summary>PennMUSH's type bits (<c>TYPE_ROOM</c> and so on in hdrs/flags.h).</summary>
	private static int TypeBits(PennMUSHObjectType type) => type switch
	{
		PennMUSHObjectType.Room => 0x1,
		PennMUSHObjectType.Thing => 0x2,
		PennMUSHObjectType.Exit => 0x4,
		_ => 0x8
	};

	/// <summary>
	/// PennMUSH 1.8 keeps an exit's aliases in its ALIAS attribute rather than its name, so a generated
	/// exit's name has none.
	/// </summary>
	private static string GenerateObjectName(int dbref, PennMUSHObjectType type) => type switch
	{
		PennMUSHObjectType.Room => $"Test Room {dbref}",
		PennMUSHObjectType.Thing => $"Test Object {dbref}",
		PennMUSHObjectType.Exit => $"Exit {dbref}",
		_ => $"Player{dbref}"
	};

	private static string RandomWords(string[] from, int atMost)
		=> string.Join(" ", Enumerable.Range(0, Random.Next(0, atMost)).Select(_ => from[Random.Next(from.Length)]).Distinct());

	private static string GenerateRandomText(int length)
	{
		const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,;:!?-'\"()[]{}";
		var sb = new StringBuilder(length);

		for (var i = 0; i < length; i++)
		{
			sb.Append(chars[Random.Next(chars.Length)]);
		}

		return sb.ToString();
	}
}
