using System.Globalization;

namespace SharpMUSH.Library.Services.DatabaseConversion.Rhost;

/// <summary>What a RhostMUSH mail dump holds once read: the messages, and each player's folder names.</summary>
/// <param name="Mail">The messages, in the model a PennMUSH maildb is read into.</param>
/// <param name="Folders">Each recipient's folders besides the inbox, by the number their messages carry.</param>
/// <param name="Notes">What did not come across, one line each.</param>
public sealed record RhostMail(
	PennMUSHMailDatabase Mail,
	IReadOnlyDictionary<int, IReadOnlyDictionary<int, string>> Folders,
	IReadOnlyList<string> Notes);

/// <summary>
/// Reads the text dump of RhostMUSH's mail, which <c>wmail/unload</c> writes to <c>data/&lt;db&gt;.dump.mail</c>
/// and <c>data/&lt;db&gt;.dump.folder</c>.
/// </summary>
/// <remarks>
/// <para>Both files are records that end <c>\x01\n</c>, a key and then its data. In the mail file,
/// <c>C&lt;player&gt;/&lt;n&gt;</c> is the player's message <c>n</c>: <c>sender/time/key/size/</c> and three
/// status characters, then the subject; <c>E&lt;sender&gt;/&lt;key&gt;</c> is the text the key names. The
/// status characters are the message's state (<c>N</c> or <c>U</c> unread, <c>O</c> read, <c>S</c> saved,
/// <c>M</c> marked), whether it was sent anonymously (lower case), and how it came (<c>F</c> forwarded).
/// The other records are RhostMUSH's indexes and settings, rebuilt from these.</para>
/// <para>In the folder file, <c>B&lt;player&gt;/&lt;folder&gt;</c> lists the messages in that folder. Messages
/// in <c>Incoming</c>, or in no folder, land in the inbox; every other folder gets a number, in the
/// order the file names them.</para>
/// </remarks>
public static class RhostMailReader
{
	/// <summary>RhostMUSH's default folder, which is the inbox here.</summary>
	private const string Incoming = "Incoming";

	/// <summary>The highest folder number a message can carry.</summary>
	private const int MaxFolder = 15;

	/// <summary>Reads the mail dump, and the folder dump when there is one.</summary>
	public static async Task<RhostMail> ReadFilesAsync(string mailPath, string? folderPath, CancellationToken cancellationToken = default)
	{
		var mail = await File.ReadAllTextAsync(mailPath, RhostFlatfileReader.FileEncoding, cancellationToken);
		var folders = string.IsNullOrEmpty(folderPath)
			? null
			: await File.ReadAllTextAsync(folderPath, RhostFlatfileReader.FileEncoding, cancellationToken);
		return Read(mail, folders);
	}

	/// <summary>Reads portable mail companions, rejecting incomplete or ambiguous message records.</summary>
	public static async Task<RhostMail> ReadFilesStrictAsync(string mailPath, string? folderPath, CancellationToken cancellationToken = default)
	{
		var mail = await File.ReadAllTextAsync(mailPath, RhostFlatfileReader.StrictFileEncoding, cancellationToken);
		var folders = folderPath is null ? null
			: await File.ReadAllTextAsync(folderPath, RhostFlatfileReader.StrictFileEncoding, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		return ReadStrict(mail, folders);
	}

	/// <summary>Reads portable mail companions with strict record and message validation.</summary>
	public static RhostMail ReadStrict(string mailText, string? folderText)
	{
		var records = StrictRecords(mailText, "mail");
		var bodies = new Dictionary<(int, int), int>();
		foreach (var (key, body) in records.Where(r => r.Key[0] == 'E'))
			if (!bodies.TryAdd(PositivePair(key), RhostFlatfileReader.StrictFileEncoding.GetByteCount(body)))
				throw Invalid($"duplicate body key {key}");
		var headers = new HashSet<(int, int)>();
		foreach (var (key, data) in records)
		{
			if (key[0] is < 'A' or > 'P') throw Invalid($"unknown mail record {key}");
			if ("CDEI".Contains(key[0])) PositivePair(key);
			else if ("ABFGHJNO".Contains(key[0])) PlayerKey(key);
			else if ((key[0] is 'M' or 'P') ? key.Length != 1 : key.Length < 2)
				throw Invalid($"malformed mail record key {key}");
			if (key[0] != 'C') continue; // Other records are source indexes and settings, not message headers.
			if (!headers.Add(PositivePair(key))) throw Invalid($"duplicate message key {key}");
			var fields = data.Split('/', 5);
			if (fields.Length != 5
				|| !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var from) || from < 0
				|| !long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sent)
				|| sent < DateTimeOffset.MinValue.ToUnixTimeSeconds() || sent > DateTimeOffset.MaxValue.ToUnixTimeSeconds()
				|| !short.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var messageKey)
				|| (messageKey & 0x7fff) == 0
				|| !short.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)
				|| fields[4].Length < 3 || !"NUOSM".Contains(char.ToUpperInvariant(fields[4][0]))
				|| !"NROFA".Contains(char.ToUpperInvariant(fields[4][1]))
				|| !"NROFA".Contains(char.ToUpperInvariant(fields[4][2])))
				throw Invalid($"malformed message header {key}");
			// Rhost stores the BCC/to-all marker in the high bit of a signed 16-bit body key.
			var bodyKey = messageKey & 0x7fff;
			if (!bodies.TryGetValue((from, bodyKey), out var bodySize)) throw Invalid($"message {key} has no body E{from}/{bodyKey}");
			// strlen(msg) is also stored as a signed short; count raw UTF-8 bytes before newline normalization.
			if (unchecked((short)bodySize) != size) throw Invalid($"message {key}'s body byte length disagrees with its header");
		}
		if (folderText is not null)
		{
			var placement = new HashSet<(int, int)>();
			foreach (var (key, data) in StrictRecords(folderText, "folder"))
			{
				if (key[0] is < 'A' or > 'D') throw Invalid($"unknown folder record {key}");
				if (key[0] != 'B')
				{
					PlayerKey(key);
					continue;
				}
				var slash = key.IndexOf('/');
				if (slash < 2 || slash == key.Length - 1
					|| !int.TryParse(key.AsSpan(1, slash - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var player) || player < 0)
					throw Invalid($"malformed folder key {key}");
				var members = data.Split('/');
				if (!int.TryParse(members[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
					|| count < 0 || count != members.Length - 1)
					throw Invalid($"malformed folder membership {key}");
				foreach (var member in members.Skip(1))
				{
					if (!int.TryParse(member, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index <= 0
						|| !headers.Contains((player, index)) || !placement.Add((player, index)))
						throw Invalid($"invalid or duplicate folder message in {key}");
				}
			}
		}
		var result = ReadCore(mailText, folderText, true);
		if (records.Any(r => r.Key[0] is >= 'F' and <= 'P'))
			result = result with { Notes = [.. result.Notes, "RhostMUSH mail settings, aliases and unfinished drafts are not imported."] };
		return result;

		static void PlayerKey(string key)
		{
			if (!int.TryParse(key.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var player) || player < 0)
				throw Invalid($"malformed player key {key}");
		}

		static (int, int) PositivePair(string key)
		{
			var pair = Pair(key, "mail");
			if (pair.Item1 < 0 || pair.Item2 <= 0) throw Invalid($"invalid message key {key}");
			return pair;
		}
	}

	private static List<(string Key, string Data)> StrictRecords(string text, string which)
	{
		var normalized = text.Replace("\x01\r\n", "\x01\n");
		if (normalized.Length > 0 && !normalized.EndsWith("\x01\n", StringComparison.Ordinal))
			throw Invalid($"{which} dump ends inside a record");
		var keys = new HashSet<string>(StringComparer.Ordinal);
		var records = Records(text, which, true).ToList();
		foreach (var (key, _) in records)
		{
			var slash = key.IndexOf('/');
			if (slash == 0) throw Invalid($"malformed {which} record key {key}");
			var canonical = key;
			// K/L name aliases literally; numeric-looking names such as 01 and 1 are distinct.
			var numeric = (which == "folder" ? "ABCD" : "ABCDEFGHIJNO").Contains(key[0]);
			if (numeric && int.TryParse(key.AsSpan(1, slash < 0 ? key.Length - 1 : slash - 1), NumberStyles.Integer,
				CultureInfo.InvariantCulture, out var first))
			{
				canonical = key[0] + first.ToString(CultureInfo.InvariantCulture);
				if (slash >= 0)
				{
					var suffix = key[(slash + 1)..];
					if (which == "folder") suffix = suffix.ToUpperInvariant();
					if (which == "mail" && int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var second))
						suffix = second.ToString(CultureInfo.InvariantCulture);
					canonical += "/" + suffix;
				}
			}
			if (!keys.Add(canonical)) throw Invalid($"duplicate {which} record {key}");
		}
		return records;
	}

	private static FormatException Invalid(string message) => new($"RhostMUSH mail dump: {message}.");

	/// <summary>Reads the dumps held in memory.</summary>
	/// <exception cref="FormatException">The mail dump's records do not read.</exception>
	public static RhostMail Read(string mailText, string? folderText)
		=> ReadCore(mailText, folderText, false);

	private static RhostMail ReadCore(string mailText, string? folderText, bool normalizeRoutingKeys)
	{
		var notes = new List<string>();
		var headers = new List<(int To, int Index, string Data)>();
		var bodies = new Dictionary<(int Sender, int Key), string>();

		foreach (var (key, data) in Records(mailText, "mail"))
		{
			switch (key[0])
			{
				case 'C':
					var (to, index) = Pair(key, "mail");
					headers.Add((to, index, data));
					break;
				case 'E':
					bodies[Pair(key, "mail")] = data;
					break;
			}
		}

		var placement = folderText is null ? [] : FolderPlacement(folderText, notes);
		if (folderText is null && headers.Count > 0)
		{
			notes.Add("No mail folder dump came with the mail, so every message is imported into its recipient's inbox.");
		}

		var messages = new List<PennMUSHMailMessage>();
		var anonymous = 0;
		var saved = 0;
		var missingBodies = 0;
		foreach (var (to, index, data) in headers)
		{
			var fields = data.Split('/', 5);
			if (fields.Length != 5
					|| !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var from)
					|| !long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sent)
					|| !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var messageKey)
					|| fields[4].Length < 3)
			{
				notes.Add($"Mail message {index} to #{to} was not imported: its header \"{data}\" does not read.");
				continue;
			}

			if (normalizeRoutingKeys) messageKey &= 0x7fff;
			var status = fields[4][..3];
			var subject = fields[4][3..];
			if (!bodies.TryGetValue((from, messageKey), out var body))
			{
				missingBodies++;
				body = string.Empty;
			}

			var flags = char.ToUpperInvariant(status[0]) switch
			{
				'O' => 0x1,
				'S' => 0x1,
				'M' => 0x1 | 0x40,
				_ => 0
			};
			if (char.ToUpperInvariant(status[0]) == 'S') saved++;
			if (char.IsLower(status[1])) anonymous++;
			if (char.ToUpperInvariant(status[2]) == 'F') flags |= 0x80;

			var folder = placement.GetValueOrDefault((to, index)).Folder;
			flags |= folder << 8;

			messages.Add(new PennMUSHMailMessage(to, from, 0, AsctimeOf(sent), subject, body, flags));
		}

		if (saved > 0)
		{
			notes.Add($"{saved} mail message(s) were saved in RhostMUSH; SharpMUSH has no saved state, so they are imported as read.");
		}

		if (anonymous > 0)
		{
			notes.Add($"{anonymous} mail message(s) were sent anonymously; SharpMUSH shows their sender.");
		}

		if (missingBodies > 0)
		{
			notes.Add($"{missingBodies} mail message(s) name a text the mail dump does not hold; they are imported empty.");
		}

		var folders = placement.Values
			.GroupBy(p => p.Player)
			.ToDictionary(g => g.Key, g => (IReadOnlyDictionary<int, string>)g.Where(p => p.Folder > 0)
				.DistinctBy(p => p.Folder).ToDictionary(p => p.Folder, p => p.Name));

		return new RhostMail(
			new PennMUSHMailDatabase
			{
				Flags = PennMUSHMailDatabase.SubjectFlag,
				MessageCount = messages.Count,
				Messages = messages
			},
			folders,
			notes);
	}

	/// <summary>
	/// Which folder each message is in, by recipient and message number: the folder's number here, and its
	/// name, which is upper-cased as SharpMUSH names folders.
	/// </summary>
	private static Dictionary<(int Player, int Index), (int Folder, string Name, int Player)> FolderPlacement(string text,
		List<string> notes)
	{
		var placement = new Dictionary<(int, int), (int, string, int)>();
		var numbers = new Dictionary<int, Dictionary<string, int>>();
		var overflow = new SortedSet<int>();
		foreach (var (key, data) in Records(text, "folder"))
		{
			if (key[0] != 'B')
			{
				continue;
			}

			var slash = key.IndexOf('/');
			if (slash < 0 || !int.TryParse(key.AsSpan(1, slash - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var player))
			{
				throw new FormatException($"RhostMUSH folder dump: the record \"{key}\" does not name a player and folder.");
			}

			var name = key[(slash + 1)..];
			if (!numbers.TryGetValue(player, out var mine))
			{
				numbers[player] = mine = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			}

			int folder;
			if (name.Equals(Incoming, StringComparison.OrdinalIgnoreCase))
			{
				folder = 0;
			}
			else if (!mine.TryGetValue(name, out folder))
			{
				folder = mine.Count + 1;
				if (folder > MaxFolder)
				{
					overflow.Add(player);
					folder = 0;
				}
				else
				{
					mine[name] = folder;
				}
			}

			foreach (var index in data.Split('/').Skip(1))
			{
				if (int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out var message))
				{
					placement[(player, message)] = (folder, name.ToUpperInvariant().Replace(' ', '_'), player);
				}
			}
		}

		if (overflow.Count > 0)
		{
			notes.Add($"Players with more than {MaxFolder} mail folders have the rest's messages in their inbox: " +
				string.Join(" ", overflow.Select(p => $"#{p}")));
		}

		return placement;
	}

	/// <summary>The dump's records as key and data pairs.</summary>
	private static IEnumerable<(string Key, string Data)> Records(string text, string which, bool preserveBodyLineEndings = false)
	{
		var records = (preserveBodyLineEndings ? text.Replace("\x01\r\n", "\x01\n") : text.Replace("\r\n", "\n"))
			.Split("\x01\n");
		var count = records.Length - (records[^1].Trim().Length == 0 ? 1 : 0);
		if (count % 2 != 0)
		{
			throw new FormatException($"RhostMUSH {which} dump: it ends with a key that has no data; the file is cut short.");
		}

		for (var i = 0; i < count; i += 2)
		{
			var key = records[i];
			if (key.Length == 0)
			{
				throw new FormatException($"RhostMUSH {which} dump: record {i / 2 + 1} has an empty key.");
			}

			yield return (key, records[i + 1]);
		}
	}

	private static (int, int) Pair(string key, string which)
	{
		var slash = key.IndexOf('/');
		if (slash < 0
				|| !int.TryParse(key.AsSpan(1, slash - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var first)
				|| !int.TryParse(key.AsSpan(slash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var second))
		{
			throw new FormatException($"RhostMUSH {which} dump: the record \"{key}\" does not read.");
		}

		return (first, second);
	}

	/// <summary>A time as PennMUSH's maildb writes it, in this machine's local time, as the import reads it back.</summary>
	private static string AsctimeOf(long seconds)
		=> DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture);
}
