using System.Text.RegularExpressions;
using SharpMUSH.Library.Services.DatabaseConversion.Softcode;

namespace SharpMUSH.Library.Services.DatabaseConversion.Rhost;

/// <summary>
/// Turns a RhostMUSH flatfile into the <see cref="PennMUSHDatabase"/> model the import writes from, and
/// says what it decided on the way, in <see cref="PennMUSHDatabase.ImportNotes"/>.
/// </summary>
/// <remarks>
/// <para>Flags, toggles and powers go through <see cref="RhostNameTables"/>: to SharpMUSH's own where one
/// fits (RhostMUSH's wizard and councilor levels to the WIZARD and ROYALTY roles, its architects to the
/// builder role), else to a flag or power of their own that the import defines, else they are dropped.
/// Every decision is a note, with the objects it touched.</para>
/// <para>Attribute values are rewritten for SharpMUSH where RhostMUSH reads them differently: colour
/// substitutions (<see cref="ColourSubstitutionRewriter"/>), character codes and literal <c>timefmt()</c>
/// formats (<see cref="RhostSoftcodeRewriter"/>). Each attribute that changed is a note of its own.</para>
/// </remarks>
public static partial class RhostDatabaseTranslator
{
	/// <summary>The longest value SharpMUSH's own softcode buffers expect; a longer one is noted.</summary>
	private const int LongValue = 8192;

	/// <summary>RhostMUSH's HOME, in a link field.</summary>
	private const int Home = -3;

	private const string ServerName = "RhostMUSH";

	/// <summary>
	/// The options RhostMUSH softcode needs. It writes literal parentheses unescaped (<c>paren_groups</c>),
	/// reads <c>trim()</c>, <c>edit()</c>, <c>lattr()</c>, <c>printf()</c>, <c>map()</c> and <c>list()</c> as
	/// RhostMUSH defines them, and lets only a SIDEFX object run side-effect functions (<c>sidefx_flag</c>).
	/// </summary>
	public static Dictionary<string, object> CompatibilityOptions() => new(StringComparer.OrdinalIgnoreCase)
	{
		["paren_groups"] = true,
		["tiny_trim_fun"] = true,
		["rhost_edit"] = true,
		["rhost_lattr"] = true,
		["rhost_printf"] = true,
		["rhost_map_args"] = true,
		["sidefx_flag"] = true,
		["rhost_list"] = true
	};

	/// <summary>Reads a flatfile and, when given, the mail dumps, into the import's model.</summary>
	public static async Task<PennMUSHDatabase> ReadAsync(string flatfilePath, string? mailPath, string? folderPath,
		CancellationToken cancellationToken = default)
	{
		var flatfile = await RhostFlatfileReader.ReadFileAsync(flatfilePath, cancellationToken);
		RhostMail? mail = null;
		string? mailError = null;
		if (!string.IsNullOrEmpty(mailPath))
		{
			try
			{
				mail = await RhostMailReader.ReadFilesAsync(mailPath, folderPath, cancellationToken);
			}
			catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
			{
				// Like a PennMUSH maildb: a bad mail dump costs the mail, not the world.
				mailError = ex.Message;
			}
		}

		var database = Translate(flatfile, mail);
		if (mailError is not null)
		{
			database.Mail = new PennMUSHMailDatabase { ReadError = mailError };
		}

		return database;
	}

	/// <summary>The import's model of <paramref name="flatfile"/>, with <paramref name="mail"/>'s messages.</summary>
	public static PennMUSHDatabase Translate(RhostFlatfile flatfile, RhostMail? mail = null)
	{
		var report = new Report();
		report.Line($"Read a {ServerName} flatfile, format version {flatfile.FormatVersion}, with {flatfile.Objects.Count} " +
			$"object(s) and {flatfile.UserAttributes.Count} user attribute name(s).");

		var byNumber = flatfile.Objects.ToDictionary(o => o.DBRef);
		var objects = new List<PennMUSHObject>(flatfile.Objects.Count);
		var definitions = new Definitions();
		var folderAttributes = FolderAttributes(mail);

		foreach (var source in flatfile.Objects)
		{
			if (Translated(source, flatfile, byNumber, definitions, folderAttributes, report) is { } translated)
			{
				objects.Add(translated);
			}
		}

		var database = new PennMUSHDatabase
		{
			Version = $"{ServerName} flatfile +V{flatfile.Version}",
			Dialect = DatabaseDialect.RhostMUSH,
			SourceOptions = CompatibilityOptions(),
			Objects = objects,
			FlagDefinitions = definitions.Flags,
			PowerDefinitions = definitions.Powers,
			GodPlayer = 1
		};

		if (mail is not null)
		{
			database.Mail = mail.Mail;
			report.Lines.AddRange(mail.Notes);
		}

		report.Line($"{ServerName} comsys channels are not imported: a stock {ServerName} runs its channels in softcode " +
			"(SOFTCOM), which comes across with the objects that hold it; a game built with the hardcoded comsys keeps each " +
			"player's channels in the CHANNELS attribute, which is imported as an attribute and not turned into channels.");
		report.Line($"Imported from a {ServerName} flatfile: the import turns on paren_groups, tiny_trim_fun, rhost_edit, " +
			"rhost_lattr, rhost_printf, rhost_map_args, sidefx_flag and rhost_list, so the imported softcode reads its " +
			"parentheses, trim(), edit(), lattr(), printf(), map() and list() the RhostMUSH way and its SIDEFX flags count.");
		database.ImportNotes = report.Finish();
		return database;
	}

	private static PennMUSHObject? Translated(RhostFlatfileObject source, RhostFlatfile flatfile,
		Dictionary<int, RhostFlatfileObject> byNumber, Definitions definitions,
		Dictionary<int, string> folderAttributes, Report report)
	{
		var type = source.Type switch
		{
			RhostObjectType.Room => PennMUSHObjectType.Room,
			RhostObjectType.Thing => PennMUSHObjectType.Thing,
			RhostObjectType.Exit => PennMUSHObjectType.Exit,
			RhostObjectType.Player => PennMUSHObjectType.Player,
			RhostObjectType.Zone => PennMUSHObjectType.Thing,
			_ => (PennMUSHObjectType?)null
		};

		if (type is not { } objectType)
		{
			report.Note("Destroyed objects (garbage) not imported", source.DBRef);
			return null;
		}

		if (source.Type == RhostObjectType.Zone)
		{
			report.Note("Zone-type objects imported as things", source.DBRef);
		}

		var flags = new List<string>();
		var powers = new List<string>();
		var denied = new List<string>();
		MapBits(source, flags, definitions, powers, report);
		MapPowers(source, powers, denied, definitions, report);

		var (location, exits, link) = Fields(source, objectType, report);
		var zone = Zone(source, byNumber, report);

		var password = default(string);
		var aliases = new List<string>();
		long created = 0;
		long modified = 0;
		var attributes = new List<PennMUSHAttribute>();
		var locks = new Dictionary<string, PennMUSHLock>();

		foreach (var attribute in source.Attributes)
		{
			switch (attribute.Number)
			{
				case RhostNameTables.PasswordAttribute when objectType == PennMUSHObjectType.Player:
					password = attribute.Value;
					NotePassword(attribute.Value, source.DBRef, report);
					continue;
				case RhostNameTables.CreatedAttribute:
					created = Stamp(attribute.Value, source.DBRef, "creation", report);
					continue;
				case RhostNameTables.ModifiedAttribute:
					modified = Stamp(attribute.Value, source.DBRef, "modification", report);
					continue;
				case RhostNameTables.AliasAttribute:
					aliases.AddRange(attribute.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
					break;
			}

			if (RhostNameTables.LockAttributes.TryGetValue(attribute.Number, out var lockName))
			{
				locks[lockName] = new PennMUSHLock(LockKey(attribute.Value), Creator: attribute.Owner);
				report.Note($"Lock attribute {RhostNameTables.BuiltInAttributes[attribute.Number]}: imported as the {lockName} lock", source.DBRef);
				continue;
			}

			if (Attribute(attribute, source, flatfile, report) is { } imported)
			{
				attributes.Add(imported);
			}
		}

		if (!locks.ContainsKey("Basic") && source.Lock.Length > 0)
		{
			locks["Basic"] = new PennMUSHLock(LockKey(BareNumbers().Replace(source.Lock, "#$1")));
		}

		if (folderAttributes.TryGetValue(source.DBRef, out var folders))
		{
			attributes.Add(new PennMUSHAttribute { Name = "MAILFOLDERS", Value = folders });
		}

		if (source.Pennies != 0)
		{
			report.Note("Pennies not imported: SharpMUSH does not track money", source.DBRef);
		}

		return new PennMUSHObject
		{
			DBRef = source.DBRef,
			Name = source.Name,
			Type = objectType,
			Location = location,
			Contents = source.Contents,
			Exits = exits,
			Link = link,
			Next = source.Next,
			Owner = source.Owner,
			Parent = source.Parent,
			Zone = zone,
			Flags = flags,
			Powers = powers,
			DeniedPowers = denied,
			CreationTime = created,
			ModificationTime = modified,
			Attributes = attributes,
			Locks = locks,
			Password = password,
			Aliases = aliases
		};
	}

	/// <summary>
	/// Where the object is and what it links to, sorted out of RhostMUSH's overloaded fields: an exit's
	/// location field is where it leads and its exits field where it is; a room's link is its drop-to, a
	/// thing's or player's its home.
	/// </summary>
	private static (int Location, int Exits, int Link) Fields(RhostFlatfileObject source, PennMUSHObjectType type, Report report)
	{
		switch (type)
		{
			case PennMUSHObjectType.Exit:
				if (source.Location == Home)
				{
					report.Note("Exits that lead HOME left unlinked, as a PennMUSH import leaves them", source.DBRef);
				}
				else if (source.Location < -1)
				{
					report.Note("Exits with a variable destination left unlinked", source.DBRef);
				}

				return (source.Exits, -1, source.Location);

			case PennMUSHObjectType.Room:
				if (source.Link == Home)
				{
					report.Note("Rooms whose drop-to is HOME: the drop-to is not imported", source.DBRef);
				}

				return (-1, source.Exits, source.Link);

			default:
				return (source.Location, -1, source.Link);
		}
	}

	/// <summary>
	/// The object's zone. RhostMUSH keeps a list of them on each side: a ZONEMASTER's names its members,
	/// any other object's its zones. SharpMUSH has one zone per object, so the first one is kept.
	/// </summary>
	private static int Zone(RhostFlatfileObject source, Dictionary<int, RhostFlatfileObject> byNumber, Report report)
	{
		var zones = source.Zones
			.Where(z => byNumber.TryGetValue(z, out var zone) && (zone.Flags[2] & ZoneMasterBit) != 0 && z != source.DBRef)
			.ToList();
		if (zones.Count > 1)
		{
			report.Note($"Objects in more than one zone: only the first is kept, as SharpMUSH has one zone per object", source.DBRef);
		}

		return zones.Count > 0 ? zones[0] : -1;
	}

	private const uint ZoneMasterBit = 0x20;

	private static void MapBits(RhostFlatfileObject source, List<string> flags, Definitions definitions, List<string> powers,
		Report report)
	{
		foreach (var bit in RhostNameTables.Bits.Concat(RhostNameTables.ToggleBits))
		{
			var word = bit.Word < 4 ? source.Flags[bit.Word] : source.Toggles[bit.Word - 4];
			if ((word & bit.Bit) == 0)
			{
				continue;
			}

			flags.AddRange(bit.Flags);
			powers.AddRange(bit.Powers);
			if (bit.Custom is { } custom)
			{
				flags.Add(custom);
				definitions.Flag(custom, bit.StaffOnly);
			}

			report.Note(Decision(bit), source.DBRef);
		}

		foreach (var (wordIndex, bitValue, name) in RhostNameTables.ToggleNames)
		{
			if ((source.Toggles[wordIndex - 4] & bitValue) != 0
					&& !RhostNameTables.ToggleBits.Any(b => b.Word == wordIndex && b.Bit == bitValue))
			{
				report.Note($"Toggle {name}: dropped, as SharpMUSH has no such setting", source.DBRef);
			}
		}
	}

	private static string Decision(RhostBitMapping bit)
	{
		var kind = bit.Word < 4 ? "Flag" : "Toggle";
		var parts = new List<string>();
		parts.AddRange(bit.Flags.Select(f => f is "WIZARD" or "ROYALTY" ? $"the {f} flag and role" : $"the {f} flag"));
		parts.AddRange(bit.Powers.Select(p => p is "Builder" or "Guest" ? $"the {p} power and role" : $"the {p} power"));
		if (bit.Custom is { } custom)
		{
			parts.Add($"a flag of its own, {custom} ({(bit.StaffOnly ? "wizards set it" : "anyone may set it")})");
		}

		return parts.Count == 0
			? $"{kind} {bit.Name}: dropped, {bit.Skip}"
			: $"{kind} {bit.Name}: imported as {string.Join(" and ", parts)}";
	}

	private static void MapPowers(RhostFlatfileObject source, List<string> powers, List<string> denied, Definitions definitions,
		Report report)
	{
		foreach (var power in RhostNameTables.Powers)
		{
			var level = (source.Toggles[power.Word] >> power.Shift) & 0x3;
			if (level == 0)
			{
				continue;
			}

			var name = power.Power ?? power.Name;
			if (power.Power is null)
			{
				definitions.Power(power.Name);
			}

			if (!powers.Contains(name, StringComparer.OrdinalIgnoreCase))
			{
				powers.Add(name);
			}

			report.Note(power.Power is null
				? $"Power {power.Name}: imported as a power of its own, {power.Name}"
				: $"Power {power.Name}: imported as the {power.Power} power", source.DBRef);
			report.Note($"Power levels: {ServerName} grants a power at a level (guildmaster, architect, councilor); a " +
				"SharpMUSH power is held or not, so each is imported as held whatever its level", source.DBRef);
		}

		foreach (var depower in RhostNameTables.Depowers)
		{
			var level = (source.Toggles[depower.Word] >> depower.Shift) & 0x3;
			if (level == 0)
			{
				continue;
			}

			if (depower.Power is { } taken)
			{
				denied.Add(taken);
				report.Note($"Depower {depower.Name}: imported as a deny on the {taken} power's permission", source.DBRef);
			}
			else
			{
				report.Note($"Depower {depower.Name}: dropped, as SharpMUSH has no power it takes away", source.DBRef);
			}
		}
	}

	/// <summary>One attribute as the import writes it, or null for one that is not imported.</summary>
	private static PennMUSHAttribute? Attribute(RhostFlatfileAttribute attribute, RhostFlatfileObject source,
		RhostFlatfile flatfile, Report report)
	{
		if ((attribute.Flags & DeletedFlag) != 0)
		{
			return null;
		}

		string name;
		uint definitionFlags = 0;
		if (attribute.Number is > 0 and < 256)
		{
			if (RhostNameTables.SkippedAttributes.TryGetValue(attribute.Number, out var reason))
			{
				report.Note($"Attribute {Builtin(attribute.Number)}: not imported, {reason}", source.DBRef);
				return null;
			}

			if (RhostNameTables.UnmatchedLockAttributes.TryGetValue(attribute.Number, out var meaning))
			{
				report.Note($"Lock {Builtin(attribute.Number)} ({meaning}): SharpMUSH has no such lock, so it is kept as an " +
					"attribute and checks nothing", source.DBRef);
			}

			if (!RhostNameTables.BuiltInAttributes.ContainsKey(attribute.Number))
			{
				report.Note($"Attribute number {attribute.Number}: not imported, {ServerName} defines no attribute by it", source.DBRef);
				return null;
			}

			name = RhostNameTables.RenamedAttributes.TryGetValue(attribute.Number, out var renamed)
				? renamed
				: Builtin(attribute.Number).ToUpperInvariant();
			if (renamed is not null)
			{
				report.Note($"Attribute {Builtin(attribute.Number)}: imported as {renamed}, SharpMUSH's name for it", source.DBRef);
			}
		}
		else if (attribute.Number >= 256)
		{
			if (!flatfile.UserAttributes.TryGetValue(attribute.Number, out var definition))
			{
				report.Note($"Attributes numbered with no +A header to name them: not imported", source.DBRef);
				return null;
			}

			if (RhostNameTables.SkippedUserAttributes.TryGetValue(definition.Name, out var reason))
			{
				report.Note($"Attribute {definition.Name}: not imported, {reason}", source.DBRef);
				return null;
			}

			name = definition.Name.ToUpperInvariant();
			definitionFlags = definition.Flags;
		}
		else
		{
			name = attribute.Name.ToUpperInvariant();
		}

		var value = Rewritten(attribute.Value, source.DBRef, name, report);
		if (value.Length > LongValue)
		{
			report.Line($"#{source.DBRef}/{name}: {value.Length} characters long, more than the {LongValue} SharpMUSH's softcode " +
				"buffers expect; check it reads whole.");
		}

		return new PennMUSHAttribute
		{
			Name = name,
			Owner = attribute.Owner,
			Flags = AttributeFlags(attribute.Flags | definitionFlags, source.DBRef, report),
			Value = value
		};
	}

	private const uint DeletedFlag = 0x80;

	private static string Builtin(int number) => RhostNameTables.BuiltInAttributes.GetValueOrDefault(number, $"#{number}");

	private static List<string> AttributeFlags(uint bits, int dbref, Report report)
	{
		var flags = new List<string>();
		foreach (var flag in RhostNameTables.AttributeFlags)
		{
			if ((bits & flag.Bit) == 0 || flag.Note == string.Empty)
			{
				continue;
			}

			if (flag.Flag is { } sharp)
			{
				if (!flags.Contains(sharp))
				{
					flags.Add(sharp);
				}

				report.Note(flag.Note is null
					? $"Attribute flag {flag.Name}: imported as {sharp}"
					: $"Attribute flag {flag.Name}: imported as {sharp}; {flag.Note}", dbref);
			}
			else
			{
				report.Note($"Attribute flag {flag.Name}: dropped, as SharpMUSH has no such attribute flag", dbref);
			}
		}

		return flags;
	}

	/// <summary>The value with the softcode rewrites made, each attribute that changed noted on its own line.</summary>
	private static string Rewritten(string value, int dbref, string name, Report report)
	{
		var colour = ColourSubstitutionRewriter.Rewrite(value,
			new ColourRewriteOptions(ColourSubstitutionDialect.RhostMush, ColourRewriteMode.Auto));
		var rest = RhostSoftcodeRewriter.Rewrite(colour.Text);

		var said = new List<string>();
		if (colour.Changed)
		{
			said.Add("%c/%x colour written as ansi()");
		}

		said.AddRange(colour.Flags.Select(flag => $"check by hand: {flag}"));
		said.AddRange(rest.Notes);
		if (said.Count > 0)
		{
			report.Line($"#{dbref}/{name}: {string.Join("; ", said)}");
		}

		return rest.Text;
	}

	/// <summary>
	/// A lock as SharpMUSH reads it. RhostMUSH's lock attributes already write objects as <c>#n</c>, and
	/// its key syntax (<c>&amp;</c>, <c>|</c>, <c>!</c>, <c>=</c>, <c>+</c>, <c>$</c>, <c>@</c>, <c>attr:value</c>)
	/// is the one SharpMUSH reads.
	/// </summary>
	private static string LockKey(string text) => text.Trim();

	[GeneratedRegex(@"(?<![#\w])(\d+)(?!\w)")]
	private static partial Regex BareNumbers();

	private static long Stamp(string text, int dbref, string which, Report report)
	{
		if (PennMUSHDatabaseConverter.TryParsePennTime(text, out var time))
		{
			return time.ToUnixTimeSeconds();
		}

		report.Note($"Objects whose {which} time does not read: dated at the import", dbref);
		return 0;
	}

	private static void NotePassword(string stored, int dbref, Report report)
	{
		const string Rehashed = "; each that verifies is rehashed at its next login";
		const string Unchecked = ", which SharpMUSH cannot check: a wizard sets these players a new password";
		var form = stored switch
		{
			"" => "nothing, so these players are locked until a wizard sets a password",
			_ when stored.StartsWith("$6$", StringComparison.Ordinal) => "SHA-512-crypt" + Rehashed,
			_ when stored.StartsWith("$5$", StringComparison.Ordinal) => "SHA-256-crypt" + Rehashed,
			_ when stored.StartsWith("$1$", StringComparison.Ordinal) => "MD5-crypt" + Rehashed,
			_ when stored.StartsWith("$2", StringComparison.Ordinal) => "bcrypt" + Unchecked,
			_ when stored.StartsWith("$SHA1$", StringComparison.OrdinalIgnoreCase) => "TinyMUX SHA-1" + Rehashed,
			_ when stored.StartsWith('$') => "an unrecognised $-form" + Unchecked,
			_ when stored.Length == 13 && stored.StartsWith("XX", StringComparison.Ordinal) => "DES crypt" + Rehashed,
			_ when stored.StartsWith("XX", StringComparison.Ordinal) && stored.Skip(2).All(char.IsAsciiDigit) => "SHS" + Rehashed,
			_ => "plaintext, or a form checked as plaintext" + Rehashed
		};
		report.Note($"Passwords stored as {form}", dbref);
	}

	/// <summary>Each recipient's synthesized MAILFOLDERS attribute, from the folder dump's folder names.</summary>
	private static Dictionary<int, string> FolderAttributes(RhostMail? mail)
		=> mail is null
			? []
			: mail.Folders.Where(f => f.Value.Count > 0).ToDictionary(f => f.Key,
				f => string.Join(" ", f.Value.OrderBy(v => v.Key).Select(v => $"{v.Key}:{v.Value}:{v.Key}")));

	/// <summary>The flags and powers of its own the import defines, each once.</summary>
	private sealed class Definitions
	{
		public List<PennMUSHFlagDefinition> Flags { get; } = [];
		public List<PennMUSHFlagDefinition> Powers { get; } = [];

		public void Flag(string name, bool staffOnly)
		{
			if (Flags.Any(f => f.Name == name))
			{
				return;
			}

			string[] perms = staffOnly ? ["wizard"] : [];
			Flags.Add(new PennMUSHFlagDefinition { Name = name, SetPermissions = [.. perms], UnsetPermissions = [.. perms] });
		}

		public void Power(string name)
		{
			if (Powers.Any(p => p.Name == name))
			{
				return;
			}

			Powers.Add(new PennMUSHFlagDefinition { Name = name, SetPermissions = ["wizard"], UnsetPermissions = ["wizard"] });
		}
	}

	/// <summary>The notes: single lines, and decisions gathered with the objects they touched.</summary>
	private sealed class Report
	{
		private readonly SortedDictionary<string, List<int>> _decisions = new(StringComparer.Ordinal);

		public List<string> Lines { get; } = [];

		public void Line(string line) => Lines.Add(line);

		public void Note(string decision, int dbref)
		{
			if (!_decisions.TryGetValue(decision, out var dbrefs))
			{
				_decisions[decision] = dbrefs = [];
			}

			if (dbrefs.Count == 0 || dbrefs[^1] != dbref)
			{
				dbrefs.Add(dbref);
			}
		}

		public List<string> Finish()
		{
			var lines = new List<string>(Lines.Count + _decisions.Count);
			lines.Add(Lines[0]);
			foreach (var (decision, dbrefs) in _decisions)
			{
				var sample = string.Join(" ", dbrefs.Take(10).Select(d => $"#{d}"));
				lines.Add($"{decision} ({dbrefs.Count} object(s): {sample}{(dbrefs.Count > 10 ? " ..." : string.Empty)})");
			}

			lines.AddRange(Lines.Skip(1));
			return lines;
		}
	}
}
