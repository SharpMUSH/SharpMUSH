using System.Globalization;

namespace SharpMUSH.Library.Services.DatabaseConversion.TinyMux;

/// <summary>
/// Reads TinyMUX's <c>comsys.db</c> (versions 4 and 5) into the chat model the importer converts, and
/// each player's channel aliases into <c>CHANALIAS`&lt;alias&gt;</c> attributes, where SharpMUSH's
/// <c>addcom</c> keeps them.
/// </summary>
/// <remarks>
/// <para>The file is <c>+V5</c> (or <c>+V4</c>), <c>*** Begin CHANNELS ***</c>, a count of players and,
/// for each, <c>dbref count</c> and that many <c>alias channel</c> lines; then
/// <c>*** Begin COMSYS ***</c>, a count of channels and, for each, its name, its header, the line
/// <c>type temp1 temp2 charge owner amount_col messages channel_object</c>, a count of users and, for
/// each, <c>dbref on title_shown gag</c> (version 4 has no gag) followed by <c>t:title</c>.</para>
/// <para>A channel's join, transmit and receive locks are the DefaultLock, UseLock and EnterLock of its
/// channel object, which TinyMUX also runs MOGRIFY` attributes on.</para>
/// </remarks>
public static class TinyMuxComsysReader
{
	private const int PlayerJoin = 0x1;
	private const int PlayerTransmit = 0x2;
	private const int PlayerReceive = 0x4;
	private const int ObjectJoin = 0x10;
	private const int ObjectTransmit = 0x20;
	private const int ObjectReceive = 0x40;
	private const int Loud = 0x100;
	private const int Public = 0x200;
	private const int Spoof = 0x400;

	/// <summary>PennMUSH's channel bits the converter reads.</summary>
	private const int PennPlayer = 0x1;
	private const int PennObject = 0x2;
	private const int PennQuiet = 0x8;

	/// <summary>PennMUSH's <c>CU_QUIET</c>: the member does not hear joins and leaves.</summary>
	private const int UserQuiet = 0x1;

	/// <summary>What the file holds: the channels and the aliases each player had for them.</summary>
	public sealed record Comsys(PennMUSHChatDatabase Chat, Dictionary<int, List<(string Alias, string Channel)>> Aliases);

	public static async Task<Comsys> ReadFileAsync(string path, CancellationToken cancellationToken = default)
	{
		await using var stream = File.OpenRead(path);
		using var text = new StreamReader(stream, TinyMuxFlatfileReader.EncodingFor(false), false);
		return await Task.Run(() => Read(text, Path.GetFileName(path)), cancellationToken);
	}

	public static Comsys Read(TextReader text, string description = "comsys.db")
	{
		var reader = new TinyMuxFlatfileReader(text, description);
		var version = reader.ReadLine() switch
		{
			"+V5" => 5,
			"+V4" => 4,
			var other => throw reader.Error($"'{other}' is not a comsys.db this importer reads (+V4 or +V5; " +
				"load and save the game once in a current TinyMUX to update an older one)")
		};

		Expect(reader, "*** Begin CHANNELS ***");
		var aliases = new Dictionary<int, List<(string Alias, string Channel)>>();
		var players = reader.ReadInt();
		for (var i = 0; i < players; i++)
		{
			var (who, count) = TwoNumbers(reader);
			var list = aliases.TryGetValue(who, out var existing) ? existing : aliases[who] = [];
			for (var j = 0; j < count; j++)
			{
				var line = reader.ReadLine() ?? throw reader.Error("the file ends inside a player's channel aliases");
				var space = line.IndexOf(' ');
				if (space > 0)
				{
					list.Add((line[..space], line[(space + 1)..]));
				}
			}
		}

		Expect(reader, "*** Begin COMSYS ***");
		var chat = new PennMUSHChatDatabase();
		var channels = reader.ReadInt();
		for (var i = 0; i < channels; i++)
		{
			var name = reader.ReadLine() ?? throw reader.Error("the file ends where a channel name was expected");
			var header = reader.ReadLine() ?? throw reader.Error("the file ends where a channel header was expected");
			var numbers = Numbers(reader, 8);
			var users = new List<PennMUSHChannelUser>();
			var userCount = reader.ReadInt();
			for (var j = 0; j < userCount; j++)
			{
				var state = Numbers(reader, version >= 5 ? 4 : 3);
				var titleLine = reader.ReadLine() ?? string.Empty;
				var title = titleLine.StartsWith("t:", StringComparison.Ordinal) ? titleLine[2..] : string.Empty;
				users.Add(new PennMUSHChannelUser(state[0],
					(version >= 5 && state[3] != 0 ? UserQuiet : 0)
					| (state[1] == 0 ? TinyMuxUserOff : 0)
					| (state[2] == 0 ? TinyMuxTitlesOff : 0),
					title));
			}

			chat.Channels.Add(new PennMUSHChannel(
				Name: TinyMuxColour.Strip(name),
				Description: header,
				Flags: numbers[0],
				Creator: numbers[4],
				Cost: numbers[3],
				Buffer: 0,
				Mogrifier: numbers[7],
				Locks: [],
				Users: users));
		}

		return new Comsys(chat, aliases);
	}

	/// <summary>Marks, cleared again by <see cref="Apply"/>, for a user who had the channel off or titles hidden.</summary>
	private const int TinyMuxUserOff = 0x10000;
	private const int TinyMuxTitlesOff = 0x20000;

	/// <summary>
	/// Puts the comsys into <paramref name="database"/>: TinyMUX's channel bits as PennMUSH's, the locks
	/// from each channel object, the aliases as attributes, and a note for everything that does not carry.
	/// </summary>
	public static void Apply(PennMUSHDatabase database, Comsys comsys, int version)
	{
		var objects = database.Objects.ToDictionary(o => o.DBRef);
		var notes = database.ImportNotes;
		var channels = new List<PennMUSHChannel>();
		foreach (var channel in comsys.Chat.Channels)
		{
			var label = $"Channel {channel.Name}";
			var type = channel.Flags;
			var flags = ((type & PlayerJoin) != 0 ? PennPlayer : 0)
				| ((type & ObjectJoin) != 0 ? PennObject : 0)
				| ((type & Loud) == 0 ? PennQuiet : 0);

			if ((type & (PlayerTransmit | PlayerReceive)) != (PlayerTransmit | PlayerReceive)
				|| (type & (ObjectTransmit | ObjectReceive)) != (ObjectTransmit | ObjectReceive))
			{
				notes.Add($"{label}: TinyMUX's per-type transmit and receive switches are not kept; " +
					"use the channel's speak lock to limit who may talk");
			}

			if ((type & Spoof) != 0)
			{
				notes.Add($"{label}: SPOOF (comtitles replace the speaker's name) is not kept, as SharpMUSH has no such channel setting");
			}

			if ((type & Public) == 0)
			{
				notes.Add($"{label}: TinyMUX listed it only to its members and staff; it is listed to everyone who passes its see lock here");
			}

			var header = TinyMuxColour.ToAnsi(channel.Description, version);
			if (!TinyMuxColour.Strip(channel.Description).Equals($"[{channel.Name}]", StringComparison.Ordinal))
			{
				notes.Add($"{label}: its header '{TinyMuxColour.Strip(channel.Description)}' is kept as the channel's description; " +
					"SharpMUSH shows a channel's name in its messages");
			}

			var owner = channel.Creator;
			if (!objects.TryGetValue(owner, out var ownerObject) || ownerObject.Type != PennMUSHObjectType.Player)
			{
				owner = objects.TryGetValue(channel.Mogrifier, out var channelObject) ? channelObject.Owner : database.GodPlayer;
			}

			var locks = new Dictionary<string, string>();
			if (objects.TryGetValue(channel.Mogrifier, out var lockHolder))
			{
				CopyLock(lockHolder, "Basic", "join", locks);
				CopyLock(lockHolder, "Use", "speak", locks);
				if (CopyLock(lockHolder, "Enter", "see", locks))
				{
					notes.Add($"{label}: its receive lock (the channel object's EnterLock) is imported as the see lock, " +
						"which also hides the channel from those who fail it");
				}
			}

			var users = new List<PennMUSHChannelUser>();
			var off = 0;
			var titlesOff = 0;
			foreach (var user in channel.Users)
			{
				if ((user.Flags & TinyMuxUserOff) != 0) off++;
				if ((user.Flags & TinyMuxTitlesOff) != 0) titlesOff++;
				users.Add(user with
				{
					Flags = user.Flags & UserQuiet,
					Title = TinyMuxColour.ToAnsi(user.Title, version)
				});
			}

			if (off > 0)
			{
				notes.Add($"{label}: {off} member(s) had it turned off; they are members here and hear it again");
			}

			if (titlesOff > 0)
			{
				notes.Add($"{label}: {titlesOff} member(s) had comtitles hidden, which SharpMUSH does not keep per member");
			}

			channels.Add(channel with
			{
				Description = header,
				Flags = flags,
				Creator = owner,
				Mogrifier = objects.ContainsKey(channel.Mogrifier) ? channel.Mogrifier : -1,
				Locks = locks,
				Users = users
			});
		}

		database.Chat = new PennMUSHChatDatabase { Channels = channels };

		var aliasCount = 0;
		foreach (var (who, aliases) in comsys.Aliases)
		{
			if (!objects.TryGetValue(who, out var holder))
			{
				notes.Add($"Channel aliases of #{who} not imported: it is not in the flatfile");
				continue;
			}

			foreach (var (alias, channel) in aliases)
			{
				var name = $"CHANALIAS`{alias.ToUpperInvariant()}";
				if (holder.Attributes.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
				holder.Attributes.Add(new PennMUSHAttribute { Name = name, Value = channel });
				aliasCount++;
			}
		}

		if (aliasCount > 0)
		{
			notes.Add($"{aliasCount} comsys alias(es) imported as CHANALIAS` attributes, which addcom, delcom and comlist use; " +
				"the comsys administration commands (@ccreate, @cset, allcom, clearcom ...) are not available");
		}
	}

	private static bool CopyLock(PennMUSHObject holder, string objectLock, string channelLock, Dictionary<string, string> locks)
	{
		if (!holder.Locks.TryGetValue(objectLock, out var key) || key.Expression.Length == 0) return false;
		locks[channelLock] = key.Expression;
		return true;
	}

	private static void Expect(TinyMuxFlatfileReader reader, string marker)
	{
		var line = reader.ReadLine();
		if (line != marker)
		{
			throw reader.Error($"expected '{marker}', found '{line}'");
		}
	}

	private static (int, int) TwoNumbers(TinyMuxFlatfileReader reader)
	{
		var numbers = Numbers(reader, 2);
		return (numbers[0], numbers[1]);
	}

	private static int[] Numbers(TinyMuxFlatfileReader reader, int count)
	{
		var line = reader.ReadLine() ?? throw reader.Error($"the file ends where {count} numbers were expected");
		var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < count)
		{
			throw reader.Error($"'{line}' has fewer than {count} numbers");
		}

		return [.. parts.Take(count).Select(part => int.TryParse(part, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
			? n
			: throw reader.Error($"'{part}' is not a number"))];
	}
}
