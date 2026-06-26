using SharpMUSH.Library.Services.DatabaseConversion;
using System.Text;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Generates large fake PennMUSH databases for performance testing.
/// </summary>
public static class PennMUSHDatabaseGenerator
{
	private static readonly string[] CommonFlags = ["SAFE", "DARK", "VISUAL", "TRANSPARENT", "WIZARD", "ROYALTY", "INHERIT", "DEBUG", "GOING", "MONITOR", "MYOPIC", "PUPPET", "CHOWN_OK", "ENTER_OK", "LINK_OK", "OPAQUE", "QUIET", "STICKY", "UNFINDABLE", "LIGHT", "HAVEN", "ABODE", "FLOATING", "TRACK_MONEY", "AUDITORIUM", "ANSI"];

	private static readonly string[] CommonPowers = ["Login", "Guest", "See_All", "Cemit", "Can_Boot", "Announce", "Halt", "Pemit_All", "Hide", "See_Queue", "Search", "No_Pay", "No_Quota", "Long_Fingers", "Unkillable", "Give"];

	private static readonly string[] CommonLockTypes = ["Basic", "Enter", "Use", "Leave", "Drop", "Give", "Page", "Mail", "Teleport", "Speech", "Listen", "Command", "Parent", "Zone"];

	private static readonly string[] AttributeNames = ["DESCRIBE", "DESCRIPTION", "SEX", "RACE", "CLASS", "LEVEL", "HP", "MANA", "STR", "DEX", "CON", "INT", "WIS", "CHA", "INVENTORY", "EQUIPMENT", "SKILLS", "SPELLS", "COMBAT", "STATS", "NOTES", "HISTORY", "TITLE", "FULLNAME", "ALIAS", "COLOR", "PROFILE", "STATUS", "MOOD", "QUOTE"];

	private static readonly Random Random = new();

	/// <summary>
	/// Generates a fake PennMUSH database file with the specified target size.
	/// </summary>
	/// <param name="targetSizeBytes">Target file size in bytes (e.g., 10MB = 10 * 1024 * 1024)</param>
	/// <returns>Path to the generated database file</returns>
	public static async Task<string> GenerateLargeDatabaseFileAsync(int targetSizeBytes = 10 * 1024 * 1024)
	{
		var tempFile = Path.Combine(Path.GetTempPath(), $"pennmush_test_{Guid.NewGuid()}.db");
		await using var writer = new StreamWriter(tempFile, false, Encoding.UTF8);

		await writer.WriteLineAsync("~0");
		await writer.WriteLineAsync("1");
		await writer.WriteLineAsync($"!{DateTime.UtcNow.ToFileTimeUtc()}");

		var currentSize = new FileInfo(tempFile).Length;
		var dbref = 0;

		while (currentSize < targetSizeBytes)
		{
			var objType = (PennMUSHObjectType)Random.Next(0, 4);
			await WriteObjectAsync(writer, dbref, objType);
			dbref++;

			if (dbref % 10 == 0)
			{
				await writer.FlushAsync();
				currentSize = new FileInfo(tempFile).Length;
			}
		}

		await writer.WriteLineAsync("***END OF DUMP***");
		await writer.FlushAsync();

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
		await using var writer = new StreamWriter(tempFile, false, Encoding.UTF8);

		await writer.WriteLineAsync("~0");
		await writer.WriteLineAsync("1");
		await writer.WriteLineAsync($"!{DateTime.UtcNow.ToFileTimeUtc()}");

		for (var dbref = 0; dbref < objectCount; dbref++)
		{
			var objType = (PennMUSHObjectType)Random.Next(0, 4);
			await WriteObjectAsync(writer, dbref, objType);
		}

		await writer.WriteLineAsync("***END OF DUMP***");
		await writer.FlushAsync();

		return tempFile;
	}

	private static async Task WriteObjectAsync(StreamWriter writer, int dbref, PennMUSHObjectType type)
	{
		var name = GenerateObjectName(dbref, type);
		var location = dbref > 0 ? Random.Next(-1, dbref) : -1;
		// Owner must be #1 (God) for all objects to ensure it exists
		// In real PennMUSH databases, most objects are owned by players, but for testing we need valid owners
		var owner = 1;
		var creationTime = DateTimeOffset.UtcNow.AddDays(-Random.Next(0, 365)).ToUnixTimeSeconds();
		var modTime = DateTimeOffset.UtcNow.AddDays(-Random.Next(0, 30)).ToUnixTimeSeconds();
		var pennies = Random.Next(0, 10000);

		// Object header: name location contents exits link next owner parent zone pennies
		await writer.WriteLineAsync($"!{dbref}");
		await writer.WriteLineAsync(name);
		await writer.WriteLineAsync($"{location}"); // location
		await writer.WriteLineAsync("-1"); // contents
		await writer.WriteLineAsync("-1"); // exits
		await writer.WriteLineAsync("-1"); // link
		await writer.WriteLineAsync("-1"); // next
		await writer.WriteLineAsync($"{owner}"); // owner
		await writer.WriteLineAsync("-1"); // parent
		await writer.WriteLineAsync("-1"); // zone
		await writer.WriteLineAsync($"{pennies}"); // pennies

		await writer.WriteLineAsync(((int)type).ToString());

		var flagCount = Random.Next(0, 6);
		var flags = Enumerable.Range(0, flagCount)
			.Select(_ => CommonFlags[Random.Next(CommonFlags.Length)])
			.Distinct()
			.ToList();
		await writer.WriteLineAsync(string.Join(" ", flags));

		var powerCount = Random.Next(0, 4);
		var powers = Enumerable.Range(0, powerCount)
			.Select(_ => CommonPowers[Random.Next(CommonPowers.Length)])
			.Distinct()
			.ToList();
		await writer.WriteLineAsync(string.Join(" ", powers));

		await writer.WriteLineAsync("");

		await writer.WriteLineAsync(creationTime.ToString());

		await writer.WriteLineAsync(modTime.ToString());

		if (type == PennMUSHObjectType.Player)
		{
			await writer.WriteLineAsync("$SHA1$1234$abcdefghijklmnopqrstuvwxyz0123456789");
		}
		else
		{
			await writer.WriteLineAsync("");
		}

		var attrCount = Random.Next(10, 101);
		for (var i = 0; i < attrCount; i++)
		{
			await WriteAttributeAsync(writer, owner);
		}

		var lockCount = Random.Next(1, 6);
		for (var i = 0; i < lockCount; i++)
		{
			await WriteLockAsync(writer);
		}

		await writer.WriteLineAsync("<");
	}

	private static async Task WriteAttributeAsync(StreamWriter writer, int defaultOwner)
	{
		var attrName = AttributeNames[Random.Next(AttributeNames.Length)];

		if (Random.Next(0, 10) < 2)
		{
			attrName += "`" + Random.Next(1, 100);
		}

		// Owner must always be #1 (God) to ensure it exists
		var owner = 1;
		var flagCount = Random.Next(0, 3);
		var flags = Enumerable.Range(0, flagCount)
			.Select(_ => new[] { "no_command", "visual", "regexp", "case", "locked", "mortal_dark", "hidden", "prefixmatch", "veiled", "debug" }[Random.Next(10)])
			.Distinct()
			.ToList();
		var derefCount = Random.Next(0, 100);

		// Attribute header: <name^owner^flags^derefs>
		var flagStr = flags.Count > 0 ? string.Join(" ", flags) : "";
		await writer.WriteLineAsync($"<{attrName}^{owner}^{flagStr}^{derefCount}>");

		var valueLength = Random.Next(50, 501);
		var value = GenerateRandomText(valueLength);

		if (Random.Next(0, 10) < 3)
		{
			value = $"\x1b[{Random.Next(30, 38)}m{value}\x1b[0m";
		}

		await writer.WriteLineAsync(value);
	}

	private static async Task WriteLockAsync(StreamWriter writer)
	{
		var lockType = CommonLockTypes[Random.Next(CommonLockTypes.Length)];
		var lockValue = $"#{Random.Next(0, 100)}";

		if (Random.Next(0, 10) < 4)
		{
			lockValue += $"|#{Random.Next(0, 100)}";
		}

		await writer.WriteLineAsync($"_{lockType}^{lockValue}");
	}

	private static string GenerateObjectName(int dbref, PennMUSHObjectType type)
	{
		return type switch
		{
			PennMUSHObjectType.Room => $"Test Room {dbref}",
			PennMUSHObjectType.Thing => $"Test Object {dbref}",
			PennMUSHObjectType.Exit => $"Exit {dbref};e{dbref}",
			PennMUSHObjectType.Player => $"Player{dbref}",
			_ => $"Object {dbref}"
		};
	}

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
