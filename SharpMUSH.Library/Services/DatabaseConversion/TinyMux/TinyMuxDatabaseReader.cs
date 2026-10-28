using System.Globalization;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Services.DatabaseConversion.Softcode;
using SharpMUSH.Library.Softcode;

namespace SharpMUSH.Library.Services.DatabaseConversion.TinyMux;

/// <summary>
/// Reads a TinyMUX flatfile (the <c>t5x</c> format <c>db_write</c> writes, versions 1 to 5) into the
/// model the PennMUSH importer converts, so one converter writes both. Everything TinyMUX stores by
/// number (types, flags, powers, attribute numbers and flags, locks) is named here, by
/// <see cref="TinyMuxNames"/>, and every choice that is not exact is recorded in
/// <see cref="PennMUSHDatabase.ImportNotes"/>.
/// </summary>
/// <remarks>
/// <para>The file is a header (<c>+X</c> version and feature bits, <c>+S</c> size, <c>+N</c> next
/// attribute number, <c>-R</c> record players, and one <c>+A</c> per user-named attribute), one
/// <c>!dbref</c> record per object, and <c>***END OF DUMP***</c>. A record is the name, location, zone,
/// contents, exits, link, next, the default lock (versions 1 and 2 only), owner, parent, money (unless
/// kept in an attribute), three flag words, two power words, then <c>&gt;number</c> and value pairs up to
/// <c>&lt;</c>. A value may start with <c>\x01owner:flags:</c>.</para>
/// <para>Versions 1 and 2 are Latin-1 with raw ANSI colour; 3 and later are UTF-8 with colour as
/// private-use code points (<see cref="TinyMuxColour"/>). A file whose attributes are kept in a separate
/// database (<c>V_DATABASE</c>) carries no attribute values and is refused.</para>
/// <para>Attribute text has its <c>%c</c>/<c>%x</c> colour substitutions rewritten as <c>ansi()</c>
/// (<see cref="ColourSubstitutionRewriter"/>), each attribute that changed noted on its own line.</para>
/// </remarks>
public static partial class TinyMuxDatabaseReader
{
	private const uint VersionMask = 0xFF;
	private const uint DatabaseFlag = 0x400;
	private const uint AttributeNameFlag = 0x800;
	private const uint AttributeKeyFlag = 0x1000;
	private const uint AttributeMoneyFlag = 0x8000;

	/// <summary>The SHA-1 prefix TinyMUX puts before a password it carried over from PennMUSH.</summary>
	private const string PennPasswordPrefix = "$P6H$$";

	/// <summary>
	/// The options TinyMUX softcode needs. MUX tests and/or/xor numerically, cuts justified text, collates,
	/// runs list() as a loop, reads ##/#@/#$ from the innermost loop, trims before characters and reads
	/// non-numbers as 0, and runs trigger() and verb(); it allows 500 levels of recursion and 32 KB values. It reads TinyMUX's names for
	/// functions and commands SharpMUSH has under its own (<c>dialect</c>).
	/// </summary>
	public static Dictionary<string, object> CompatibilityOptions() => new(StringComparer.OrdinalIgnoreCase)
	{
		["paren_groups"] = true,
		["mux_logic"] = true,
		["mux_justify"] = true,
		["mux_collation"] = true,
		["rhost_list"] = true,
		["innermost_tokens"] = true,
		["tiny_trim_fun"] = true,
		["tiny_math"] = true,
		["null_eq_zero"] = true,
		["mux_queue_functions"] = true,
		["function_recursion_limit"] = 500u,
		["dialect"] = SoftcodeDialects.Mux.Key
	};

	/// <summary>Where the read model's <see cref="PennMUSHDatabase.Configuration"/> records the flatfile version.</summary>
	public const string FlatfileVersionKey = "tinymux_flatfile_version";

	/// <summary>The flatfile version a model read here came from, which decides how its companion files' colour reads.</summary>
	public static int FlatfileVersion(PennMUSHDatabase database)
		=> database.Configuration.TryGetValue(FlatfileVersionKey, out var text)
			&& int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
				? version
				: 5;

	/// <summary>Whether the file starts with a TinyMUX flatfile header, <c>+X</c> and a number.</summary>
	public static bool IsTinyMuxFlatfile(string path)
	{
		try
		{
			using var stream = File.OpenRead(path);
			return IsTinyMuxFlatfile(stream);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	/// <summary>Whether the stream starts with a TinyMUX flatfile header. The stream is read from its start.</summary>
	public static bool IsTinyMuxFlatfile(Stream stream)
	{
		var header = TinyMuxFlatfileReader.FirstLine(stream);
		return header.StartsWith("+X", StringComparison.Ordinal)
			&& long.TryParse(header.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out _);
	}

	public static async Task<PennMUSHDatabase> ReadFileAsync(string path, CancellationToken cancellationToken = default)
	{
		await using var stream = File.OpenRead(path);
		return await Task.Run(() => Read(stream, Path.GetFileName(path), cancellationToken), cancellationToken);
	}

	/// <summary>Reads a whole flatfile. The stream must be at its start.</summary>
	public static PennMUSHDatabase Read(Stream stream, string description = "TinyMUX flatfile",
		CancellationToken cancellationToken = default)
	{
		if (!stream.CanSeek)
		{
			var buffered = new MemoryStream();
			stream.CopyTo(buffered);
			buffered.Position = 0;
			stream = buffered;
		}

		var start = stream.Position;
		var header = TinyMuxFlatfileReader.FirstLine(stream);
		if (!header.StartsWith("+X", StringComparison.Ordinal)
			|| !uint.TryParse(header.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var headerValue))
		{
			throw new FormatException($"{description}: not a TinyMUX flatfile (it starts '{header}', not '+X')");
		}

		var version = (int)(headerValue & VersionMask);
		var features = headerValue & ~VersionMask;
		if (version is < 1 or > 5)
		{
			throw new FormatException($"{description}: TinyMUX flatfile version {version} is not one this importer reads (1 to 5)");
		}

		if ((features & DatabaseFlag) != 0)
		{
			throw new FormatException($"{description}: this flatfile keeps its attributes in a separate database, so it holds " +
				"no attribute values. Write a full flatfile from TinyMUX (db_unload, or ./Backup) and import that.");
		}

		stream.Position = start;
		using var text = new StreamReader(stream, TinyMuxFlatfileReader.EncodingFor(version <= 2), false, 65536, leaveOpen: true);
		var reader = new TinyMuxFlatfileReader(text, description);
		var file = new RawFile(version, features);
		ReadRecords(reader, file, cancellationToken);
		return new Mapper(file).Map();
	}

	/// <summary>The file as written, before anything is named.</summary>
	private sealed class RawFile(int version, uint features)
	{
		public int Version { get; } = version;
		public uint Features { get; } = features;
		public bool ReadsName => (Features & DatabaseFlag) == 0 || (Features & AttributeNameFlag) == 0;
		public bool ReadsHeaderLock => (Features & AttributeKeyFlag) == 0;
		public bool ReadsMoney => (Features & AttributeMoneyFlag) == 0;
		public Dictionary<int, (string Name, int Flags)> UserAttributes { get; } = [];
		public List<RawObject> Objects { get; } = [];
		public int? RecordPlayers { get; set; }
	}

	private sealed class RawObject
	{
		public required int DBRef { get; init; }
		public string Name { get; set; } = string.Empty;
		public int Location { get; set; } = -1;
		public int Zone { get; set; } = -1;
		public int Contents { get; set; } = -1;
		public int Exits { get; set; } = -1;
		public int Link { get; set; } = -1;
		public int Next { get; set; } = -1;
		public TinyMuxLockKey.Node? HeaderLock { get; set; }
		public int Owner { get; set; } = -1;
		public int Parent { get; set; } = -1;
		public int Pennies { get; set; }
		public uint Flags1 { get; set; }
		public uint Flags2 { get; set; }
		public uint Flags3 { get; set; }
		public uint Powers1 { get; set; }
		public uint Powers2 { get; set; }
		public List<(int Number, string Value)> Attributes { get; } = [];
	}

	private static void ReadRecords(TinyMuxFlatfileReader reader, RawFile file, CancellationToken cancellationToken)
	{
		while (true)
		{
			var c = reader.Read();
			switch (c)
			{
				case '+':
					ReadHeaderLine(reader, file);
					break;
				case '-':
					if (reader.Read() == 'R')
					{
						file.RecordPlayers = reader.ReadInt();
					}
					else
					{
						reader.ReadLine();
					}

					break;
				case '!':
					cancellationToken.ThrowIfCancellationRequested();
					file.Objects.Add(ReadObject(reader, file));
					break;
				case '*':
					var marker = reader.ReadLine() ?? string.Empty;
					if (!marker.StartsWith("**END OF DUMP***", StringComparison.Ordinal))
					{
						throw reader.Error($"'*{marker}' is not the end-of-dump marker");
					}

					return;
				case '\n':
					break;
				case -1:
					throw reader.Error("the file ends without its ***END OF DUMP*** marker; it may be cut short");
				default:
					throw reader.Error($"'{(char)c}' does not start a header line or an object");
			}
		}
	}

	private static void ReadHeaderLine(TinyMuxFlatfileReader reader, RawFile file)
	{
		switch (reader.Read())
		{
			case 'A':
				var number = reader.ReadInt();
				var definition = reader.ReadString();
				var flags = 0;
				var name = definition;
				var digits = 0;
				while (digits < definition.Length && char.IsAsciiDigit(definition[digits])) digits++;
				if (digits > 0 && digits < definition.Length && definition[digits] == ':')
				{
					flags = int.Parse(definition.AsSpan(0, digits), CultureInfo.InvariantCulture);
					name = definition[(digits + 1)..];
				}

				if (number >= TinyMuxNames.FirstUserAttribute)
				{
					file.UserAttributes[number] = (name.ToUpperInvariant(), flags);
				}

				break;
			default:
				// +X was read before the records; +S and +N size tables TinyMUX rebuilds itself.
				reader.ReadLine();
				break;
		}
	}

	private static RawObject ReadObject(TinyMuxFlatfileReader reader, RawFile file)
	{
		var record = new RawObject { DBRef = reader.ReadInt() };
		if (record.DBRef < 0)
		{
			throw reader.Error($"object number {record.DBRef} is negative");
		}

		if (file.ReadsName)
		{
			record.Name = reader.ReadString();
		}

		record.Location = reader.ReadInt();
		record.Zone = reader.ReadInt();
		record.Contents = reader.ReadInt();
		record.Exits = reader.ReadInt();
		record.Link = reader.ReadInt();
		record.Next = reader.ReadInt();
		if (file.ReadsHeaderLock)
		{
			record.HeaderLock = TinyMuxLockKey.ParseHeaderLock(reader);
		}

		record.Owner = reader.ReadInt();
		record.Parent = reader.ReadInt();
		if (file.ReadsMoney)
		{
			record.Pennies = reader.ReadInt();
		}

		record.Flags1 = reader.ReadWord();
		record.Flags2 = reader.ReadWord();
		record.Flags3 = reader.ReadWord();
		record.Powers1 = reader.ReadWord();
		record.Powers2 = reader.ReadWord();

		while (true)
		{
			var c = reader.Read();
			switch (c)
			{
				case '>':
					var number = reader.ReadInt();
					var value = reader.ReadString();
					if (number > 0)
					{
						record.Attributes.Add((number, value));
					}

					break;
				case '<':
					reader.ReadLine();
					return record;
				case '\n':
					break;
				case -1:
					throw reader.Error($"the file ends inside the attributes of #{record.DBRef}");
				default:
					throw reader.Error($"'{(char)c}' in the attributes of #{record.DBRef}");
			}
		}
	}

	/// <summary>
	/// Names everything in a <see cref="RawFile"/>, building the model and the notes about it.
	/// </summary>
	private sealed partial class Mapper(RawFile file)
	{
		private readonly PennMUSHDatabase _database = new()
		{
			Version = $"TinyMUX flatfile version {file.Version}",
			Configuration = { [FlatfileVersionKey] = file.Version.ToString(CultureInfo.InvariantCulture) },
			Dialect = DatabaseDialect.TinyMUX,
			SourceOptions = CompatibilityOptions(),
			SharesPipingSubstitutions = true
		};

		/// <summary>What was carried over approximately or dropped, with how often.</summary>
		private readonly SortedDictionary<string, (ImportWarningKind Kind, List<int> Dbrefs)> _tally = new(StringComparer.Ordinal);

		/// <summary>TinyMUX's own bookkeeping left out, which every import of its flatfiles leaves out.</summary>
		private readonly SortedDictionary<string, (ImportWarningKind Kind, List<int> Dbrefs)> _routine = new(StringComparer.Ordinal);
		private readonly HashSet<string> _flagsUsed = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _powersUsed = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>One line for each attribute whose colour substitutions were rewritten or need a look.</summary>
		private readonly List<SoftcodeRewrite> _rewrites = [];

		private void Tally(ImportWarningKind kind, string what, int dbref, bool routine = false)
		{
			var tally = routine ? _routine : _tally;
			if (!tally.TryGetValue(what, out var tallied))
			{
				tally[what] = tallied = (kind, []);
			}

			tallied.Dbrefs.Add(dbref);
		}

		public PennMUSHDatabase Map()
		{
			foreach (var raw in file.Objects)
			{
				if (MapObject(raw) is { } mapped)
				{
					_database.Objects.Add(mapped);
				}
			}

			CopyZoneControl();
			_database.FlagDefinitions = [.. TinyMuxNames.CustomFlags.Where(flag => _flagsUsed.Contains(flag.Name))];
			_database.PowerDefinitions = [.. TinyMuxNames.CustomPowers.Where(power => _powersUsed.Contains(power.Name))];
			Report();
			return _database;
		}

		private PennMUSHObject? MapObject(RawObject raw)
		{
			var type = (int)(raw.Flags1 & TinyMuxNames.TypeMask) switch
			{
				TinyMuxNames.TypeRoom => PennMUSHObjectType.Room,
				TinyMuxNames.TypeThing => PennMUSHObjectType.Thing,
				TinyMuxNames.TypeExit => PennMUSHObjectType.Exit,
				TinyMuxNames.TypePlayer => PennMUSHObjectType.Player,
				TinyMuxNames.TypeGarbage => (PennMUSHObjectType?)null,
				var unknown => Unknown(unknown, raw.DBRef)
			};
			if (type is null)
			{
				return null;
			}

			var attributes = new List<PennMUSHAttribute>();
			var locks = new Dictionary<string, PennMUSHLock>(StringComparer.OrdinalIgnoreCase);
			string? password = null;
			List<string> aliases = [];
			long created = 0, modified = 0;
			var auditorium = (raw.Flags2 & 0x100) != 0;

			if (raw.HeaderLock is { } headerLock)
			{
				locks["Basic"] = TinyMuxLockKey.Write(headerLock, AttributeNameOf, note => Tally(ImportWarningKind.Locks, note, raw.DBRef));
			}

			foreach (var (number, stored) in raw.Attributes)
			{
				var (owner, instanceFlags, value) = DecodeInfo(stored);
				var flags = instanceFlags | (number >= TinyMuxNames.FirstUserAttribute
					&& file.UserAttributes.TryGetValue(number, out var definition) ? definition.Flags : 0);

				switch (number)
				{
					case TinyMuxNames.PasswordAttribute:
						password = value.StartsWith(PennPasswordPrefix, StringComparison.Ordinal) ? value[PennPasswordPrefix.Length..] : value;
						continue;
					case TinyMuxNames.CreatedAttribute:
						created = Timestamp(value, raw.DBRef);
						continue;
					case TinyMuxNames.ModifiedAttribute:
						modified = Timestamp(value, raw.DBRef);
						continue;
				}

				if (TinyMuxNames.DroppedAttributes.TryGetValue(number, out var why))
				{
					Tally(ImportWarningKind.Attributes, $"Attribute number {number} not imported: {why}", raw.DBRef, routine: true);
					continue;
				}

				if ((flags & TinyMuxNames.AttributeDeleted) != 0)
				{
					continue;
				}

				if (TinyMuxNames.Locks.TryGetValue(number, out var lockMapping))
				{
					AddLock(raw, locks, lockMapping, number, value, owner, flags, auditorium);
					continue;
				}

				if (AttributeNameOf(number) is not { } name)
				{
					Tally(ImportWarningKind.Attributes, $"Attribute number {number} not imported: the flatfile does not name it " +
						"(TinyMUX warns of this when a backup was taken from a running game)", raw.DBRef);
					continue;
				}

				if (number == TinyMuxNames.AliasAttribute)
				{
					aliases = [.. value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
				}

				attributes.Add(new PennMUSHAttribute
				{
					Name = name,
					Owner = owner >= 0 && owner != raw.Owner ? owner : null,
					Flags = AttributeFlagNames(flags, name, raw.DBRef),
					Value = TinyMuxColour.ToAnsi(Rewritten(value, raw.DBRef, name), file.Version)
				});
			}

			var (flagNames, flagPowers) = Flags(raw);
			var (powerNames, permissions) = Powers(raw);
			return new PennMUSHObject
			{
				DBRef = raw.DBRef,
				Name = TinyMuxColour.Strip(raw.Name),
				Type = type.Value,
				// TinyMUX overloads its location, exits and link fields by type, as PennMUSH does: an exit's
				// location is its destination and its exits field its source; a room's location is its
				// drop-to; a thing's or player's link is its home.
				Location = type switch
				{
					PennMUSHObjectType.Room => -1,
					PennMUSHObjectType.Exit => raw.Exits,
					_ => raw.Location
				},
				Exits = type == PennMUSHObjectType.Room ? raw.Exits : -1,
				Link = type switch
				{
					PennMUSHObjectType.Room or PennMUSHObjectType.Exit => raw.Location,
					_ => raw.Link
				},
				Contents = raw.Contents,
				Next = raw.Next,
				Owner = raw.Owner,
				Parent = raw.Parent,
				Zone = raw.Zone,
				Pennies = raw.Pennies,
				Flags = flagNames,
				Powers = [.. flagPowers, .. powerNames],
				Permissions = permissions,
				CreationTime = created,
				ModificationTime = modified,
				Attributes = attributes,
				Locks = locks,
				Password = password,
				Aliases = aliases
			};
		}

		private PennMUSHObjectType? Unknown(int type, int dbref)
		{
			Tally(ImportWarningKind.Objects, $"Object of unknown type {type} not imported", dbref);
			return null;
		}

		/// <summary>The SharpMUSH name of an attribute number, built-in or user-named; null for an unnamed one.</summary>
		private string? AttributeNameOf(int number)
			=> number >= TinyMuxNames.FirstUserAttribute
				? file.UserAttributes.TryGetValue(number, out var user) ? user.Name : null
				: TinyMuxNames.Attributes.GetValueOrDefault(number);

		/// <summary>
		/// Splits <c>\x01owner:flags:value</c>. A value without that prefix belongs to the object's owner
		/// and has no flags of its own.
		/// </summary>
		private static (int Owner, int Flags, string Value) DecodeInfo(string stored)
		{
			var match = InfoPrefix().Match(stored);
			return match.Success
				? (int.Parse(match.Groups["owner"].ValueSpan, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
					int.Parse(match.Groups["flags"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture),
					stored[match.Length..])
				: (-1, 0, stored);
		}

		[GeneratedRegex(@"^\x01(?<owner>-?\d+):(?<flags>\d+):")]
		private static partial Regex InfoPrefix();

		private List<string> AttributeFlagNames(int flags, string name, int dbref)
		{
			// The mux flag marks the attribute as written in TinyMUX's dialect.
			var names = new List<string> { SoftcodeDialects.Mux.Key };
			foreach (var (bit, mapping) in TinyMuxNames.AttributeFlags)
			{
				if ((flags & bit) == 0) continue;
				names.AddRange(mapping.Flags);
				if (mapping.Note is { } note)
				{
					Tally(ImportWarningKind.Attributes, $"Attribute flag {mapping.Source} {note}", dbref);
				}
			}

			return [.. names.Distinct(StringComparer.OrdinalIgnoreCase)];
		}

		private void AddLock(RawObject raw, Dictionary<string, PennMUSHLock> locks, TinyMuxNames.LockMapping mapping,
			int number, string value, int owner, int flags, bool auditorium)
		{
			string key;
			try
			{
				key = TinyMuxLockKey.Convert(TinyMuxColour.Strip(value), AttributeNameOf, note => Tally(ImportWarningKind.Locks, $"Lock: {note}", raw.DBRef));
			}
			catch (FormatException ex)
			{
				Tally(ImportWarningKind.Locks, $"{mapping.MuxName} not imported: TinyMUX's key could not be read ({ex.Message})", raw.DBRef);
				return;
			}

			if (key.Length == 0)
			{
				return;
			}

			var lockName = mapping.Lock;
			if (number == TinyMuxNames.SpeechLockAttribute && !auditorium)
			{
				// TinyMUX checks a SpeechLock only in an AUDITORIUM; SharpMUSH checks its Speech lock always.
				lockName = "SpeechLock";
				Tally(ImportWarningKind.Locks, "SpeechLock on an object that is not AUDITORIUM, where TinyMUX ignores it: imported as the " +
					"user-defined lock SpeechLock, which nothing checks, so it stays without effect", raw.DBRef);
			}
			else if (mapping.UserDefined)
			{
				Tally(ImportWarningKind.Locks, $"{mapping.MuxName} imported as the user-defined lock {lockName}: SharpMUSH has no such built-in " +
					"lock, so nothing checks it unless softcode does (elock())", raw.DBRef);
			}

			var lockFlags = ((flags & 0x800) != 0 ? 0x01 : 0) // visual
				| ((flags & 0x1000) != 0 ? 0x02 : 0) // private: no_inherit
				| ((flags & 0x4) != 0 ? 0x04 : 0) // wizard
				| ((flags & 0x40) != 0 ? 0x08 : 0) // locked
				| ((flags & 0x10000) != 0 ? 0x10 : 0); // no_clone
			locks[lockName] = new PennMUSHLock(key, lockFlags, owner >= 0 ? owner : null);
		}

		private (List<string> Flags, List<string> Powers) Flags(RawObject raw)
		{
			var flags = new List<string>();
			var powers = new List<string>();
			foreach (var (word, table, label) in new[]
				{
					(raw.Flags1 & ~(uint)TinyMuxNames.TypeMask, TinyMuxNames.Flags1, "first"),
					(raw.Flags2, TinyMuxNames.Flags2, "second"),
					(raw.Flags3, TinyMuxNames.Flags3, "third")
				})
			{
				for (var bit = 0; bit < 32; bit++)
				{
					var mask = 1u << bit;
					if ((word & mask) == 0) continue;
					if (!table.TryGetValue(mask, out var mapping))
					{
						Tally(ImportWarningKind.Flags, $"Unknown bit 0x{mask:x} of the {label} flag word not imported", raw.DBRef);
						continue;
					}

					if (mapping.Dropped is { } dropped)
					{
						Tally(ImportWarningKind.Flags, $"Flag {mapping.Source} not imported: {dropped}", raw.DBRef, routine: true);
						continue;
					}

					if (mapping.Power is { } power)
					{
						powers.Add(power);
						Tally(ImportWarningKind.Flags, $"Flag {mapping.Source} imported as the {power} power", raw.DBRef);
						continue;
					}

					foreach (var name in mapping.Flags)
					{
						flags.Add(name);
						if (TinyMuxNames.CustomFlags.Any(custom => custom.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
						{
							_flagsUsed.Add(name);
							Tally(ImportWarningKind.Flags, $"Flag {mapping.Source} imported as a custom flag {name}: objects keep it and hasflag() sees it, " +
								"but SharpMUSH does nothing with it", raw.DBRef);
						}
						else if (!name.Equals(mapping.Source, StringComparison.OrdinalIgnoreCase))
						{
							Tally(ImportWarningKind.Flags, $"Flag {mapping.Source} imported as {string.Join(" and ", mapping.Flags)}", raw.DBRef);
						}
					}
				}
			}

			if (flags.Contains("SLAVE", StringComparer.OrdinalIgnoreCase))
			{
				Tally(ImportWarningKind.Flags, "SLAVE kept as a custom flag, but SharpMUSH does not stop a SLAVE player from building or " +
					"changing anything: restrict them another way", raw.DBRef);
			}

			return ([.. flags.Distinct(StringComparer.OrdinalIgnoreCase)], powers);
		}

		private (List<string> Powers, List<string> Permissions) Powers(RawObject raw)
		{
			var powers = new List<string>();
			var permissions = new List<string>();
			foreach (var (word, table, label) in new[]
				{
					(raw.Powers1, TinyMuxNames.Powers1, "first"),
					(raw.Powers2, TinyMuxNames.Powers2, "second")
				})
			{
				for (var bit = 0; bit < 32; bit++)
				{
					var mask = 1u << bit;
					if ((word & mask) == 0) continue;
					if (!table.TryGetValue(mask, out var mapping))
					{
						Tally(ImportWarningKind.Powers, $"Unknown bit 0x{mask:x} of the {label} power word not imported", raw.DBRef);
						continue;
					}

					if (mapping.Permission is { } permission)
					{
						permissions.Add(permission);
						Tally(ImportWarningKind.Powers, $"Power {mapping.Source} granted as the {permission} permission", raw.DBRef);
						continue;
					}

					if (mapping.Power is not { } power) continue;
					powers.Add(power);
					if (TinyMuxNames.CustomPowers.Any(custom => custom.Name.Equals(power, StringComparison.OrdinalIgnoreCase)))
					{
						_powersUsed.Add(power);
						Tally(ImportWarningKind.Powers, $"Power {mapping.Source} imported as a custom power: objects keep it and haspower() sees it, " +
							"but SharpMUSH does nothing with it", raw.DBRef);
					}
					else if (!power.Equals(mapping.Source, StringComparison.OrdinalIgnoreCase))
					{
						Tally(ImportWarningKind.Powers, $"Power {mapping.Source} imported as {power}{(mapping.Source == "dark" ? " (it lets a player be DARK; TinyMUX's also hides from WHO)" : "")}", raw.DBRef);
					}
				}
			}

			return (powers, permissions);
		}

		/// <summary>
		/// A <c>Created</c> or <c>Modified</c> value, local time as TinyMUX writes it
		/// (<c>Thu Sep 04 12:01:45 2026</c>, sometimes with fractional seconds), in Unix seconds.
		/// </summary>
		private long Timestamp(string value, int dbref)
		{
			if (TryParseTime(value, out var time))
			{
				return time.ToUnixTimeSeconds();
			}

			Tally(ImportWarningKind.Objects, "A creation or modification time that could not be read was not imported", dbref);
			return 0;
		}

		/// <summary>
		/// TinyMUX controls a zone through the zone object's EnterLock; SharpMUSH, as PennMUSH, through its
		/// Zone lock. Each zone object's EnterLock is copied to its Zone lock, unless it already has one.
		/// </summary>
		private void CopyZoneControl()
		{
			var byNumber = _database.Objects.ToDictionary(o => o.DBRef);
			foreach (var zone in _database.Objects.Select(o => o.Zone).Where(zone => zone >= 0).Distinct())
			{
				if (!byNumber.TryGetValue(zone, out var zoneObject) || zoneObject.Locks.ContainsKey("Zone"))
				{
					continue;
				}

				if (zoneObject.Locks.TryGetValue("Enter", out var enter))
				{
					zoneObject.Locks["Zone"] = enter;
					Tally(ImportWarningKind.Locks, "Zone object's EnterLock copied to its Zone lock, which is what grants control of a zone here", zone);
				}
				else
				{
					Tally(ImportWarningKind.Locks, "Zone object has no EnterLock, so TinyMUX gave no one control through it; it has no Zone lock here either", zone);
				}
			}
		}

		/// <summary>
		/// The attribute's text with its <c>%c</c>/<c>%x</c> colour substitutions written as <c>ansi()</c>, which
		/// SharpMUSH reads as the last command and an attribute; each attribute that changed, or that holds
		/// colour the rewrite could not carry over exactly, is a line of its own in
		/// <see cref="PennMUSHDatabase.SoftcodeRewrites"/>. It runs on the text as stored,
		/// before the stored colour becomes escapes, so an escape's <c>[</c> is never read as a function call.
		/// </summary>
		private string Rewritten(string value, int dbref, string name)
		{
			var colour = ColourSubstitutionRewriter.Rewrite(value,
				new ColourRewriteOptions(ColourSubstitutionDialect.TinyMux, ColourRewriteMode.Auto));

			var said = new List<string>();
			if (colour.Changed)
			{
				said.Add("%c/%x colour written as ansi()");
			}

			said.AddRange(colour.Flags.Select(flag => $"check by hand: {flag}"));
			if (said.Count > 0)
			{
				// Shown to the administrator, so the stored colour, which is private-use code points, is left out.
				_rewrites.Add(new SoftcodeRewrite($"#{dbref}/{name}", string.Join("; ", said),
					TinyMuxColour.Strip(value), TinyMuxColour.Strip(colour.Text)));
			}

			return colour.Text;
		}

		private void Report()
		{
			var notes = _database.ImportNotes;
			notes.Add(new ImportWarning(ImportWarningKind.Softcode,
				"Imported from a TinyMUX flatfile: the import turns on paren_groups, mux_logic, mux_justify, mux_collation, " +
				"rhost_list, innermost_tokens, tiny_trim_fun, tiny_math, null_eq_zero and mux_queue_functions, and raises function_recursion_limit to 500, " +
				"as TinyMUX softcode expects."));
			notes.Add(new ImportWarning(ImportWarningKind.Softcode,
				"The import sets dialect to mux and gives every attribute it brings in the mux attribute flag, so that code " +
				"reads TinyMUX's names for functions and commands SharpMUSH has under its own, such as strtrunc() and @qmail."));
			notes.Add(new ImportWarning(ImportWarningKind.Softcode,
				"TinyMUX compresses runs of spaces in evaluated text and SharpMUSH keeps them; there is no option for it yet, " +
				"so output that relied on it may show extra spaces."));

			notes.AddRange(Tallied(_tally));
			_database.RoutineNotes.AddRange(Tallied(_routine).Select(tallied => tallied.Text));

			_database.SoftcodeRewrites.AddRange(_rewrites);
		}

		private static IEnumerable<ImportWarning> Tallied(SortedDictionary<string, (ImportWarningKind Kind, List<int> Dbrefs)> tally)
		{
			foreach (var (what, (kind, dbrefs)) in tally)
			{
				var distinct = dbrefs.Distinct().ToList();
				var sample = string.Join(" ", distinct.Take(10).Select(dbref => $"#{dbref}"));
				yield return new ImportWarning(kind, $"{what} ({dbrefs.Count} time(s), on {sample}{(distinct.Count > 10 ? " ..." : "")})");
			}
		}
	}

	/// <summary>Reads a time as TinyMUX writes one, in the importing machine's local time.</summary>
	public static bool TryParseTime(string value, out DateTimeOffset time)
	{
		var normalised = Fraction().Replace(Spaces().Replace(value.Trim(), " "), string.Empty);
		if (DateTime.TryParseExact(normalised, ["ddd MMM d HH:mm:ss yyyy", "ddd MMM dd HH:mm:ss yyyy"],
				CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
		{
			time = new DateTimeOffset(parsed);
			return true;
		}

		time = default;
		return false;
	}

	[GeneratedRegex(@"\s+")]
	private static partial Regex Spaces();

	[GeneratedRegex(@"(?<=:\d\d)\.\d+")]
	private static partial Regex Fraction();
}
