using System.Globalization;
using SharpMUSH.Configuration;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Services.DatabaseConversion.Softcode;
using SharpMUSH.Library.Services.Passwords;
using SharpMUSH.Library.Softcode;

namespace SharpMUSH.Library.Services.DatabaseConversion.Rhost;

/// <summary>
/// Turns a RhostMUSH flatfile into the <see cref="PennMUSHDatabase"/> model the import writes from, and
/// says what it decided on the way, in <see cref="PennMUSHDatabase.ImportNotes"/>.
/// </summary>
/// <remarks>
/// <para>Flags, toggles and powers go through <see cref="RhostNameTables"/>: to SharpMUSH's own where one
/// fits (RhostMUSH's royalty, its wizards, to the WIZARD role), else to a flag or power of their own that the
/// import defines, else they are dropped. Its other staff levels become roles of their own
/// (<see cref="BuiltInRoles.RhostLevels"/>). Every decision is a note, with the objects it touched.</para>
/// <para>Each object's reality levels are carried as its masks; the game's <c>netrhost.conf</c> names them, and
/// the import turns them into reality layers.</para>
/// <para>Attribute values are rewritten for SharpMUSH where RhostMUSH reads them differently: colour
/// substitutions (<see cref="ColourSubstitutionRewriter"/>), character codes and literal <c>timefmt()</c>
/// formats (<see cref="RhostSoftcodeRewriter"/>). Each attribute that changed is a note of its own.</para>
/// </remarks>
public static partial class RhostDatabaseTranslator
{
	/// <summary>The longest value SharpMUSH's own softcode buffers expect; a longer one is noted.</summary>
	private const int LongValue = 8192;

	private const string ServerName = "RhostMUSH";

	/// <summary>
	/// The options RhostMUSH softcode needs. It writes literal parentheses unescaped (<c>paren_groups</c>),
	/// tests <c>and()</c>, <c>or()</c> and <c>xor()</c> arguments by their leading integer (<c>mux_logic</c>), reads <c>trim()</c>, <c>edit()</c>, <c>lattr()</c>, <c>printf()</c>, <c>map()</c>, <c>list()</c>, <c>iter()</c>
	/// and <c>lrooms()</c> as RhostMUSH defines them, lets only a SIDEFX object run side-effect functions (<c>sidefx_flag</c>), lets NOEXAMINE and NOMODIFY protect
	/// (<c>protection_flags</c>), matches a NOPARSE <c>$</c>-command against the command as typed (<c>attribute_eval_flags</c>), and
	/// reads RhostMUSH's names for functions and commands SharpMUSH has under its own (<c>dialect</c>).
	/// </summary>
	public static Dictionary<string, object> CompatibilityOptions() => new(StringComparer.OrdinalIgnoreCase)
	{
		["paren_groups"] = true,
		["mux_logic"] = true,
		["tiny_trim_fun"] = true,
		["rhost_edit"] = true,
		["rhost_lattr"] = true,
		["rhost_printf"] = true,
		["rhost_map_args"] = true,
		["sidefx_flag"] = true,
		["rhost_list"] = true,
		["rhost_lrooms"] = true,
		["protection_flags"] = true,
		["attribute_eval_flags"] = true,
		["dialect"] = SoftcodeDialects.Rhost.Key,
		// RhostMUSH's mail command is mail (see the note Translate gives).
		["command_aliases"] = new Dictionary<string, string[]> { ["@MAIL"] = ["mail"] }
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
		report.Line(ImportWarningKind.Other, $"Read a {ServerName} flatfile, format version {flatfile.FormatVersion}, with {flatfile.Objects.Count} " +
			$"object(s) and {flatfile.UserAttributes.Count} user attribute name(s).");

		var byNumber = flatfile.Objects.ToDictionary(o => o.DBRef);
		var objects = new List<PennMUSHObject>(flatfile.Objects.Count);
		var definitions = new Definitions();
		var folderAttributes = FolderAttributes(mail);

		report.LocalFunctions = LocalFunctions(flatfile);
		var clusters = Clusters(flatfile, byNumber, report);
		foreach (var source in flatfile.Objects)
		{
			if (clusters.TryGetValue(source.DBRef, out var first) && first != source.DBRef)
			{
				report.Note(ImportWarningKind.Objects, $"Cluster objects: merged into their cluster's first object (#{first}), " +
					"which takes their attributes", source.DBRef);
				continue;
			}

			if (Translated(source, flatfile, byNumber, definitions, folderAttributes, report) is { } translated)
			{
				objects.Add(translated);
			}
		}

		MergeClusters(clusters, objects, flatfile, byNumber, report);

		// Every toggle's flag, carried or not, so @toggle and hastoggle() find one no object has yet.
		foreach (var toggle in RhostNameTables.ToggleBits.Where(bit => bit.Custom is not null))
		{
			definitions.Flag(toggle.Custom!, toggle.StaffOnly, RhostNameTables.Toggle(toggle.Name)?.Hidden ?? false);
		}

		var database = new PennMUSHDatabase
		{
			Version = $"{ServerName} flatfile +V{flatfile.Version}",
			Dialect = DatabaseDialect.RhostMUSH,
			SourceOptions = CompatibilityOptions(),
			Objects = objects,
			FlagDefinitions = definitions.Flags,
			Roles = [.. BuiltInRoles.RhostLevels],
			GodPlayer = 1
		};

		if (mail is not null)
		{
			database.Mail = mail.Mail;
			report.Lines.AddRange(mail.Notes.Select(note => new ImportWarning(ImportWarningKind.Mail, note)));
		}

		report.Line(ImportWarningKind.Mail, $"mail is now another name for @mail: listing and reading mail work as in {ServerName}, but " +
			$"mail <players>=<text> sends <text> at once, as @mail does, where {ServerName} would start a message with it as the subject.");
		report.Line(ImportWarningKind.Channels, $"{ServerName} comsys channels are not imported: a stock {ServerName} runs its channels in softcode " +
			"(SOFTCOM), which comes across with the objects that hold it; a game built with the hardcoded comsys keeps each " +
			"player's channels in the CHANNELS attribute, which is imported as an attribute and not turned into channels.");
		report.Line(ImportWarningKind.Softcode, $"Imported from a {ServerName} flatfile: the import turns on paren_groups, mux_logic, tiny_trim_fun, " +
			"rhost_edit, rhost_lattr, rhost_printf, rhost_map_args, sidefx_flag, rhost_list, " +
			"rhost_lrooms and protection_flags, so the imported softcode reads its parentheses, and(), or(), xor(), trim(), edit(), lattr(), " +
			"printf(), map(), list() and lrooms() the RhostMUSH way, its SIDEFX flags count and NOEXAMINE and NOMODIFY keep protecting. " +
			"It sets dialect to rhost and gives every attribute it brings in the rhost attribute flag, so that code reads " +
			$"{ServerName}'s names for functions and commands SharpMUSH has under its own, such as wildmatch() and @sudo.");
		MapTotems(database, RhostNameTables.BuiltInTotems, report);
		database.ImportNotes = report.Finish();
		database.RoutineNotes = report.FinishRoutine();
		database.SoftcodeRewrites = report.Rewrites;
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
			report.Note(ImportWarningKind.Objects, "Destroyed objects (garbage) not imported", source.DBRef);
			return null;
		}

		if (source.Type == RhostObjectType.Zone)
		{
			report.Note(ImportWarningKind.Objects, "Zone-type objects imported as things", source.DBRef);
		}

		var flags = new List<string>();
		var powers = new List<string>();
		var roles = new List<string>();
		MapBits(source, flags, definitions, powers, roles, report);
		var overrides = MapPowers(source, report);

		var (location, exits, link) = Fields(source, objectType, report);
		var zones = Zones(source, byNumber);

		var password = default(string);
		var reality = default(PennMUSHReality);
		var totems = Array.Empty<uint>();
		var tags = new List<PennMUSHTag>();
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
				case RhostNameTables.TotemsAttribute:
					totems = [.. attribute.Value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Take(10)
						.Select(word => long.TryParse(word, CultureInfo.InvariantCulture, out var bits) ? unchecked((uint)bits) : 0u)];
					continue;
				case RhostNameTables.RealityAttribute:
					reality = Reality(attribute.Value, source.DBRef, report);
					continue;
				case RhostNameTables.TagsAttribute:
					tags.AddRange(attribute.Value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(Tag));
					continue;
				case RhostNameTables.AliasAttribute:
					aliases.AddRange(attribute.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
					break;
			}

			if (RhostNameTables.LockAttributes.TryGetValue(attribute.Number, out var lockName))
			{
				locks[lockName] = new PennMUSHLock(LockKey(attribute.Value), Creator: attribute.Owner);
				report.Note(ImportWarningKind.Locks, $"Lock attribute {RhostNameTables.BuiltInAttributes[attribute.Number]}: imported as the {lockName} lock", source.DBRef);
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
			report.Note(ImportWarningKind.Objects, "Pennies not imported: SharpMUSH does not track money", source.DBRef);
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
			Zone = zones.Count > 0 ? zones[0] : -1,
			FurtherZones = [.. zones.Skip(1)],
			Flags = flags,
			Powers = powers,
			Overrides = overrides,
			Roles = roles,
			Reality = reality,
			Totems = totems,
			Tags = tags,
			CreationTime = created,
			ModificationTime = modified,
			Attributes = attributes,
			Locks = locks,
			Password = password,
			Aliases = aliases
		};
	}

	/// <summary>
	/// A tag as RhostMUSH lists it on the object it names: <c>__&lt;owner&gt;_&lt;name&gt;</c> for an owner's own. An owner
	/// number too large for a dbref leaves the name a world tag, which the import then takes or refuses like any other.
	/// </summary>
	private static PennMUSHTag Tag(string listed)
		=> PersonalTag().Match(listed) is { Success: true } personal
			&& int.TryParse(personal.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var owner)
				? new PennMUSHTag(personal.Groups[2].Value, owner)
				: new PennMUSHTag(listed, null);

	[GeneratedRegex(@"^__(\d+)_(.+)$")]
	private static partial Regex PersonalTag();

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
				// HOME and a variable destination (AMBIGUOUS) carry over as they are; the converter records them.
				if (source.Location < -1 && source.Location is not (LinkTypes.HomeDbref or LinkTypes.VariableDbref))
				{
					report.Note(ImportWarningKind.Objects, "Exits whose destination is not an object, HOME or variable left unlinked", source.DBRef);
				}

				return (source.Exits, -1, source.Location);

			case PennMUSHObjectType.Room:
				return (-1, source.Exits, source.Link);

			default:
				return (source.Location, -1, source.Link);
		}
	}

	/// <summary>
	/// The object's zones, in its order. RhostMUSH keeps a list of them on each side: a ZONEMASTER's names its members,
	/// any other object's its zones. Only the second kind are zones of the object.
	/// </summary>
	private static List<int> Zones(RhostFlatfileObject source, Dictionary<int, RhostFlatfileObject> byNumber)
		=> source.Zones
			.Where(z => byNumber.TryGetValue(z, out var zone) && (zone.Flags[2] & ZoneMasterBit) != 0 && z != source.DBRef)
			.Distinct()
			.ToList();

	private const uint ZoneMasterBit = 0x20;

	/// <summary>The role category totems go in.</summary>
	public const string TotemCategory = "Totems";

	/// <summary>
	/// The priority of a totem's role: above guest and below player, so anyone who may assign roles at all may assign
	/// it. It grants nothing.
	/// </summary>
	public const int TotemPriority = 1;

	/// <summary>
	/// Makes a role of each of <paramref name="totems"/>, a game's own from its <c>netrhost.conf</c>, and gives it to
	/// every object that holds the totem. A game's own totem is made whether or not an object holds it, as softcode may
	/// test it.
	/// </summary>
	public static void MapTotems(PennMUSHDatabase database, IEnumerable<TotemDirective> totems)
	{
		var report = new Report();
		MapTotems(database, totems.Select(totem => (totem, true)), report, always: true);
		database.ImportNotes.AddRange(report.Lines.Concat(report.Decisions()));
	}

	/// <summary>
	/// Each totem as a role with no permissions in the <see cref="TotemCategory"/> category, its slug the totem's name
	/// in lower case, given to the objects holding it. RhostMUSH's own totems are made only when an object holds one.
	/// </summary>
	private static void MapTotems(PennMUSHDatabase database, IEnumerable<(TotemDirective Totem, bool StaffOnly)> totems, Report report,
		bool always = false)
	{
		foreach (var (totem, staffOnly) in totems)
		{
			var name = totem.Name.Trim().ToUpperInvariant();
			if (name.Length == 0)
			{
				continue;
			}

			var slug = TotemSlug(name);
			var holders = database.Objects
				.Where(o => o.Totems.Count > totem.Slot && (o.Totems[totem.Slot] & totem.Value) == totem.Value && !o.Roles.Contains(slug))
				.ToArray();
			if (holders.Length == 0 && !always)
			{
				continue;
			}

			if (database.Roles.FirstOrDefault(role => role.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase)) is { } shared)
			{
				if (!shared.Name.Equals(name, StringComparison.Ordinal))
				{
					report.Line(ImportWarningKind.Flags, $"Totem {name}: shares the role {slug} with the totem {shared.Name}");
				}
			}
			else
			{
				database.Roles.Add(new SharpRole { Slug = slug, Name = name, Category = TotemCategory, Priority = TotemPriority });
				if (!database.RoleCategories.Any(category => category.Name.Equals(TotemCategory, StringComparison.OrdinalIgnoreCase)))
				{
					database.RoleCategories.Add(new RoleCategory(TotemCategory,
						$"{ServerName}'s totems: roles that grant nothing, which softcode tests with hasrole().", 0));
				}
			}

			var who = staffOnly ? "" : $"; anyone could set it in {ServerName}, here those who may assign roles do";
			foreach (var holder in holders)
			{
				holder.Roles.Add(slug);
				report.Note(ImportWarningKind.Flags, $"Totem {name}: imported as the role {slug}{who}", holder.DBRef);
			}
		}
	}

	/// <summary>The start of every totem role's slug, which keeps a totem from naming a role that allows something.</summary>
	public const string TotemSlugPrefix = "totem-";

	/// <summary>
	/// The slug of a totem's role: <c>totem-</c> and its name in lower case, any character a slug may not hold written
	/// as <c>_</c>.
	/// </summary>
	public static string TotemSlug(string name)
	{
		var slug = TotemSlugPrefix + new string([.. name.Trim().ToLowerInvariant()
			.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_')]);
		return slug[..Math.Min(slug.Length, RoleNames.MaxLength)];
	}

	/// <summary>
	/// Each object in a cluster, by number, and the cluster's first object, the one it was made on. RhostMUSH spreads a
	/// cluster's attributes over its objects to pass its limit per object; one object here holds them all. Each of a
	/// cluster's objects lists them all in its <c>_CLUSTER</c> attribute, the first object first.
	/// </summary>
	private static Dictionary<int, int> Clusters(RhostFlatfile flatfile, Dictionary<int, RhostFlatfileObject> byNumber, Report report)
	{
		var number = flatfile.UserAttributes.FirstOrDefault(entry => entry.Value.Name.Equals(RhostNameTables.ClusterAttribute,
			StringComparison.OrdinalIgnoreCase)).Key;
		var clusters = new Dictionary<int, int>();
		if (number == 0)
		{
			return clusters;
		}

		foreach (var source in flatfile.Objects)
		{
			if (clusters.ContainsKey(source.DBRef) || source.Attributes.FirstOrDefault(a => a.Number == number) is not { } listing)
			{
				continue;
			}

			var members = Dbrefs().Matches(listing.Value).Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
				.Where(byNumber.ContainsKey)
				.Distinct()
				.ToArray();
			if (members.Length == 0)
			{
				continue;
			}

			foreach (var member in members)
			{
				clusters.TryAdd(member, members[0]);
			}

			report.Line(ImportWarningKind.Objects, $"Cluster #{members[0]}: its {members.Length} object(s) merged into #{members[0]}.");
		}

		return clusters;
	}

	/// <summary>
	/// Gives each cluster's first object the user attributes of the others, in the order the cluster lists them. A name the
	/// first object, or an earlier one, already holds keeps that value, and is noted.
	/// </summary>
	private static void MergeClusters(Dictionary<int, int> clusters, List<PennMUSHObject> objects, RhostFlatfile flatfile,
		Dictionary<int, RhostFlatfileObject> byNumber, Report report)
	{
		foreach (var (member, first) in clusters.Where(entry => entry.Key != entry.Value))
		{
			if (objects.FirstOrDefault(o => o.DBRef == first) is not { } target)
			{
				continue;
			}

			var source = byNumber[member];
			foreach (var attribute in source.Attributes.Where(a => a.Number >= 256 && a.Number is not (>= RhostNameTables.InlineStart and < RhostNameTables.InlineEnd)))
			{
				if (Attribute(attribute, source, flatfile, report) is not { } imported)
				{
					continue;
				}

				if (target.Attributes.Any(a => a.Name.Equals(imported.Name, StringComparison.OrdinalIgnoreCase)))
				{
					report.Note(ImportWarningKind.Attributes, $"Cluster attribute {imported.Name}: held by more than one of the cluster's objects; " +
						$"#{first} keeps the first one's value", member);
					continue;
				}

				target.Attributes.Add(imported);
			}
		}
	}

	/// <summary>An object's reality masks (<see cref="PennMUSHReality.Parse"/>); a value RhostMUSH would not read is noted.</summary>
	private static PennMUSHReality? Reality(string value, int dbref, Report report)
	{
		if (PennMUSHReality.Parse(value) is { } reality)
		{
			return reality;
		}

		report.Note(ImportWarningKind.Objects, $"Reality levels that {ServerName} itself would not read, left off", dbref);
		return null;
	}

	private static void MapBits(RhostFlatfileObject source, List<string> flags, Definitions definitions, List<string> powers,
		List<string> roles, Report report)
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
			if (bit.Role is { } role)
			{
				roles.Add(role);
			}

			if (bit.Custom is { } custom)
			{
				flags.Add(custom);
				definitions.Flag(custom, bit.StaffOnly, bit.Word >= 4 && (RhostNameTables.Toggle(bit.Name)?.Hidden ?? false));
			}

			report.Note(ImportWarningKind.Flags, Decision(bit), source.DBRef);
		}

	}

	private static string Decision(RhostBitMapping bit)
	{
		var kind = bit.Word < 4 ? "Flag" : "Toggle";
		var parts = new List<string>();
		parts.AddRange(bit.Flags.Select(f => f is "WIZARD" or "ROYALTY" ? $"the {f} flag and role" : $"the {f} flag"));
		parts.AddRange(bit.Powers.Select(p => p is "Builder" or "Guest" ? $"the {p} power and role" : $"the {p} power"));
		if (bit.Role is { } role)
		{
			parts.Add($"the {role} role");
		}

		if (bit.Custom is { } custom)
		{
			parts.Add($"a flag of its own, {custom} ({(bit.StaffOnly ? "wizards set it" : "anyone may set it")})");
		}

		return parts.Count == 0
			? $"{kind} {bit.Name}: dropped, {bit.Skip}"
			: $"{kind} {bit.Name}: imported as {string.Join(" and ", parts)}";
	}

	/// <summary>
	/// The object's powers as allows, and its depowers and restricting flags as denies, on the permissions each lands on. A power's level is
	/// the staff level whose role its reach names, so it acts on what that level and those below own. A depower's
	/// level works the other way: its deny reaches that level and those above, or everyone at its lowest setting. A
	/// scope that takes no reach holds at every rank. Two on one scope keep the wider, and a deny beats an allow.
	/// </summary>
	internal static List<PennMUSHOverride> MapPowers(RhostFlatfileObject source, Report report)
	{
		var landed = new Dictionary<string, PennMUSHOverride>(StringComparer.OrdinalIgnoreCase);
		foreach (var power in RhostNameTables.Powers)
		{
			var level = (source.Toggles[power.Word] >> power.Shift) & 0x3;
			if (level == 0)
			{
				continue;
			}

			Land(power, PermissionState.Allow, power.Ranked ? RhostNameTables.LevelRoles[(int)level - 1] : null, landed, source.DBRef,
				report);
		}

		// RhostMUSH stores a depower's level one higher than it is, unless the player's DPSHIFT flag is set, which setting
		// any depower at councilor turns on. Either way this reads each level as RhostMUSH itself does.
		var shifted = (source.Flags[2] & RhostNameTables.DepowerShift) != 0;
		foreach (var depower in RhostNameTables.Depowers)
		{
			var stored = (source.Toggles[depower.Word] >> depower.Shift) & 0x3;
			if (stored == 0)
			{
				continue;
			}

			var level = shifted ? stored : stored - 1;
			Land(depower, PermissionState.Deny, depower.Ranked && level > 0 ? RhostNameTables.LevelRoles[(int)level - 1] : null, landed,
				source.DBRef, report);
		}

		foreach (var (bit, name, denies) in RhostNameTables.RestrictingBits)
		{
			if (source.Type != RhostObjectType.Player || (source.Flags[1] & bit) == 0)
			{
				continue;
			}

			foreach (var scope in denies)
			{
				var deny = new PennMUSHOverride(scope, PermissionState.Deny);
				landed[scope] = landed.TryGetValue(scope, out var held) ? Wider(held, deny) : deny;
			}

			report.Note(ImportWarningKind.Flags, $"Flag {name}: also imported as a deny on {string.Join(" and ", denies)}", source.DBRef);
		}

		if ((source.Flags[2] & RhostNameTables.NoOverrideBit) != 0)
		{
			foreach (var scope in RestrictingFlags.NoOverride)
			{
				landed[scope] = new PennMUSHOverride(scope, PermissionState.Deny);
			}

			report.Note(ImportWarningKind.Flags,
				$"Flag NO_OVERRIDE: also imported as a deny on {string.Join(" and ", RestrictingFlags.NoOverride)}",
				source.DBRef);
		}

		return [.. landed.Values];
	}

	private static void Land(RhostPowerMapping mapping, PermissionState state, string? reach,
		Dictionary<string, PennMUSHOverride> landed, int dbref, Report report)
	{
		var kind = state == PermissionState.Allow ? "Power" : "Depower";
		if (mapping.Scopes.Length == 0)
		{
			report.Note(ImportWarningKind.Powers, $"{kind} {mapping.Name}: dropped, as {mapping.Skip}", dbref,
				needsAction: state == PermissionState.Deny);
			return;
		}

		foreach (var scope in mapping.Scopes)
		{
			var next = new PennMUSHOverride(scope, state, reach is not null && PortalPermission.TakesReach(scope) ? reach : null);
			if (landed.TryGetValue(scope, out var held) && held is not null && held.State != next.State
					&& (held.State == PermissionState.Deny ? held : next) is { Reach: { } kept })
			{
				// One override per scope: the deny stays, and outside its reach the roles decide, not the power.
				report.Note(ImportWarningKind.Powers, $"Power and depower both on {scope}: only the deny, reaching {kept}, is kept, so " +
					$"the power no longer applies below {kept}; give it back there by hand if it should", dbref, needsAction: true);
			}

			landed[scope] = held is null ? next : Wider(held, next);
		}

		var unreached = reach is null ? [] : mapping.Scopes.Where(scope => !PortalPermission.TakesReach(scope)).ToArray();
		var how = reach is null
			? mapping.Ranked && state == PermissionState.Deny ? ", at every rank" : ""
			: unreached.Length == 0
				? $", reaching {reach}"
				: unreached.Length == mapping.Scopes.Length
					? $"; held at {reach} in {ServerName}, but no reach can bound it here, so it holds at every rank"
					: $", reaching {reach}; {string.Join(" and ", unreached)} takes no reach, so it holds at every rank";
		report.Note(ImportWarningKind.Powers,
			$"{kind} {mapping.Name}: imported as {(state == PermissionState.Allow ? "an allow" : "a deny")} on {string.Join(" and ", mapping.Scopes)}{how}",
			dbref, needsAction: state == PermissionState.Allow && unreached.Length > 0);
	}

	/// <summary>Of two overrides on one scope, the one that does more: a deny, else the one with the wider reach.</summary>
	private static PennMUSHOverride Wider(PennMUSHOverride held, PennMUSHOverride next)
		=> (held, next) switch
		{
			_ when held.State != next.State => held.State == PermissionState.Deny ? held : next,
			({ Reach: null }, _) => held,
			(_, { Reach: null }) => next,
			_ => (Rank(next.Reach!) > Rank(held.Reach!)) == (next.State == PermissionState.Allow) ? next : held,
		};

	private static int Rank(string level) => RhostNameTables.LevelRoles.ToList().IndexOf(level);

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
		if (attribute.Number is (> 0 and < 256) or (>= RhostNameTables.InlineStart and < RhostNameTables.InlineEnd))
		{
			if (RhostNameTables.SkippedAttributes.TryGetValue(attribute.Number, out var reason))
			{
				report.Note(ImportWarningKind.Attributes, $"Attribute {Builtin(attribute.Number)}: not imported, {reason}", source.DBRef, routine: true);
				return null;
			}

			if (RhostNameTables.UnmatchedLockAttributes.TryGetValue(attribute.Number, out var meaning))
			{
				report.Note(ImportWarningKind.Locks, $"Lock {Builtin(attribute.Number)} ({meaning}): SharpMUSH has no such lock, so it is kept as an " +
					"attribute and checks nothing", source.DBRef);
			}

			if (!RhostNameTables.BuiltInAttributes.ContainsKey(attribute.Number))
			{
				report.Note(ImportWarningKind.Attributes, $"Attribute number {attribute.Number}: not imported, {ServerName} defines no attribute by it", source.DBRef);
				return null;
			}

			name = RhostNameTables.RenamedAttributes.TryGetValue(attribute.Number, out var renamed)
				? renamed
				: Builtin(attribute.Number).ToUpperInvariant();
			if (renamed is not null)
			{
				report.Note(ImportWarningKind.Attributes, $"Attribute {Builtin(attribute.Number)}: imported as {renamed}, SharpMUSH's name for it", source.DBRef);
			}
		}
		else if (attribute.Number >= 256)
		{
			if (!flatfile.UserAttributes.TryGetValue(attribute.Number, out var definition))
			{
				report.Note(ImportWarningKind.Attributes, $"Attributes numbered with no +A header to name them: not imported", source.DBRef);
				return null;
			}

			if (RhostNameTables.SkippedUserAttributes.TryGetValue(definition.Name, out var reason))
			{
				report.Note(ImportWarningKind.Attributes, $"Attribute {definition.Name}: not imported, {reason}", source.DBRef);
				return null;
			}

			name = definition.Name.ToUpperInvariant();
			definitionFlags = definition.Flags;
		}
		else
		{
			name = attribute.Name.ToUpperInvariant();
		}

		var value = Rewritten(attribute.Value, source.DBRef, name, report,
			report.LocalFunctions.GetValueOrDefault(source.Owner) ?? Empty);
		if (value.Length > LongValue)
		{
			report.Line(ImportWarningKind.Attributes, $"#{source.DBRef}/{name}: {value.Length} characters long, more than the {LongValue} SharpMUSH's softcode " +
				"buffers expect; check it reads whole.");
		}

		return new PennMUSHAttribute
		{
			Name = name,
			Owner = attribute.Owner,
			Flags = name == LiveWire.AttributeName
				? [.. LiveWire.AttributeFlags]
				: AttributeFlags(attribute.Flags | definitionFlags, source.DBRef, report),
			Value = value
		};
	}

	private const uint DeletedFlag = 0x80;

	private static string Builtin(int number) => RhostNameTables.BuiltInAttributes.GetValueOrDefault(number, $"#{number}");

	/// <summary>
	/// The SharpMUSH attribute flags <paramref name="bits"/> become, and the <c>rhost</c> flag, which marks the
	/// attribute as written in RhostMUSH's dialect.
	/// </summary>
	private static List<string> AttributeFlags(uint bits, int dbref, Report report)
	{
		var flags = new List<string> { SoftcodeDialects.Rhost.Key };
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

				report.Note(ImportWarningKind.Attributes, flag.Note is null
					? $"Attribute flag {flag.Name}: imported as {sharp}"
					: $"Attribute flag {flag.Name}: imported as {sharp}; {flag.Note}", dbref);
			}
			else
			{
				report.Note(ImportWarningKind.Attributes, $"Attribute flag {flag.Name}: dropped, as SharpMUSH has no such attribute flag", dbref);
			}
		}

		return flags;
	}

	private static readonly HashSet<string> Empty = [];

	/// <summary>
	/// The local functions each owner's code defines with <c>@lfunction</c>. RhostMUSH files one under the owner of
	/// the object that ran the command, and only that owner's objects call it, so a call is rewritten in their code;
	/// not a name a global <c>@function</c> also takes, which RhostMUSH calls first.
	/// </summary>
	private static Dictionary<int, HashSet<string>> LocalFunctions(RhostFlatfile flatfile)
	{
		// A global @function of the same name is called first, so its calls stay as written.
		var global = flatfile.Objects
			.SelectMany(source => source.Attributes.SelectMany(attribute => RhostCommandRewriter.GlobalFunctionNames(attribute.Value)))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		return flatfile.Objects
			.SelectMany(source => source.Attributes.SelectMany(attribute => RhostCommandRewriter.LocalFunctionNames(attribute.Value))
				.Where(name => !global.Contains(name))
				.Select(name => (source.Owner, Name: name)))
			.GroupBy(entry => entry.Owner)
			.ToDictionary(group => group.Key, group => group.Select(entry => entry.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));
	}

	/// <summary>The value with the softcode rewrites made, each attribute that changed listed on its own line.</summary>
	private static string Rewritten(string value, int dbref, string name, Report report, IReadOnlySet<string> localFunctions)
	{
		var colour = ColourSubstitutionRewriter.Rewrite(value,
			new ColourRewriteOptions(ColourSubstitutionDialect.RhostMush, ColourRewriteMode.Auto));
		var rest = RhostSoftcodeRewriter.Rewrite(colour.Text, localFunctions);

		var said = new List<string>();
		if (colour.Changed)
		{
			said.Add("%c/%x colour written as ansi()");
		}

		said.AddRange(colour.Flags.Select(flag => $"check by hand: {flag}"));
		said.AddRange(rest.Notes);
		if (said.Count > 0)
		{
			report.Rewrites.Add(new SoftcodeRewrite($"#{dbref}/{name}", string.Join("; ", said), value, rest.Text));
		}

		return rest.Text;
	}

	/// <summary>
	/// A lock as SharpMUSH reads it. RhostMUSH's lock attributes write each object as <c>(#n)</c>, and its key
	/// syntax (<c>&amp;</c>, <c>|</c>, <c>!</c>, <c>=</c>, <c>+</c>, <c>$</c>, <c>@</c>, <c>attr:value</c>) is the one
	/// SharpMUSH reads, except that SharpMUSH wants a prefix right before the object: <c>$(#73)</c> is <c>$#73</c>.
	/// An <c>attr:value</c> or <c>attr/value</c> key's value, which runs to the next <c>&amp;</c>, <c>|</c> or
	/// <c>)</c>, is left as it is.
	/// </summary>
	private static string LockKey(string text) => PrefixedObject().Replace(text.Trim(), "$1$2");

	[GeneratedRegex(@"(?<![:/][^&|)]*)([=+$@])\((#-?\d+)\)")]
	private static partial Regex PrefixedObject();

	[GeneratedRegex(@"(?<![#\w])(\d+)(?!\w)")]
	private static partial Regex BareNumbers();

	[GeneratedRegex(@"#(\d+)")]
	private static partial Regex Dbrefs();

	private static long Stamp(string text, int dbref, string which, Report report)
	{
		if (PennMUSHDatabaseConverter.TryParsePennTime(text, out var time))
		{
			return time.ToUnixTimeSeconds();
		}

		report.Note(ImportWarningKind.Objects, $"Objects whose {which} time does not read: dated at the import", dbref);
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
			_ when PennMUSHPasswords.IsBcrypt(stored) => "bcrypt" + Rehashed,
			_ when stored.StartsWith("$SHA1$", StringComparison.OrdinalIgnoreCase) => "TinyMUX SHA-1" + Rehashed,
			_ when stored.StartsWith('$') => "an unrecognised $-form" + Unchecked,
			_ when stored.Length == 13 && stored.StartsWith("XX", StringComparison.Ordinal) => "DES crypt" + Rehashed,
			_ when stored.StartsWith("XX", StringComparison.Ordinal) && stored.Skip(2).All(char.IsAsciiDigit) => "SHS" + Rehashed,
			_ => "plaintext, or a form checked as plaintext" + Rehashed
		};
		report.Note(ImportWarningKind.Players, $"Passwords stored as {form}", dbref, needsAction: form.EndsWith(Unchecked, StringComparison.Ordinal));
	}

	/// <summary>Each recipient's synthesized MAILFOLDERS attribute, from the folder dump's folder names.</summary>
	private static Dictionary<int, string> FolderAttributes(RhostMail? mail)
		=> mail is null
			? []
			: mail.Folders.Where(f => f.Value.Count > 0).ToDictionary(f => f.Key,
				f => string.Join(" ", f.Value.OrderBy(v => v.Key).Select(v => $"{v.Key}:{v.Value}:{v.Key}")));

	/// <summary>The flags of its own the import defines, each once.</summary>
	private sealed class Definitions
	{
		public List<PennMUSHFlagDefinition> Flags { get; } = [];

		public void Flag(string name, bool staffOnly, bool hidden = false)
		{
			if (Flags.Any(f => f.Name == name))
			{
				return;
			}

			// mdark: only staff see it on an object, as RhostMUSH shows a staff toggle only to staff.
			string[] perms = (staffOnly, hidden) switch
			{
				(_, true) => ["wizard", "mdark"],
				(true, false) => ["wizard"],
				_ => []
			};
			Flags.Add(new PennMUSHFlagDefinition { Name = name, SetPermissions = [.. perms], UnsetPermissions = [.. perms] });
		}
	}

	/// <summary>The notes: single lines, and decisions gathered with the objects they touched.</summary>
	internal sealed class Report
	{
		private readonly SortedDictionary<string, (ImportWarningKind Kind, bool NeedsAction, List<int> Dbrefs)> _decisions =
			new(StringComparer.Ordinal);

		/// <summary>RhostMUSH's own bookkeeping left out, which every import of its flatfiles leaves out.</summary>
		private readonly SortedDictionary<string, (ImportWarningKind Kind, bool NeedsAction, List<int> Dbrefs)> _routine =
			new(StringComparer.Ordinal);

		public List<ImportWarning> Lines { get; } = [];

		/// <summary>The local functions each owner's code defines (<see cref="RhostDatabaseTranslator.LocalFunctions"/>).</summary>
		public Dictionary<int, HashSet<string>> LocalFunctions { get; set; } = [];

		/// <summary>The attributes whose softcode was rewritten, reported apart from the notes.</summary>
		public List<SoftcodeRewrite> Rewrites { get; } = [];

		public void Line(ImportWarningKind kind, string line) => Lines.Add(new ImportWarning(kind, line));

		public void Note(ImportWarningKind kind, string decision, int dbref, bool routine = false, bool needsAction = false)
		{
			var decisions = routine ? _routine : _decisions;
			if (!decisions.TryGetValue(decision, out var decided))
			{
				decisions[decision] = decided = (kind, needsAction, []);
			}

			if (decided.Dbrefs.Count == 0 || decided.Dbrefs[^1] != dbref)
			{
				decided.Dbrefs.Add(dbref);
			}
		}

		public List<ImportWarning> Finish()
		{
			var lines = new List<ImportWarning>(Lines.Count + _decisions.Count);
			lines.Add(Lines[0]);
			lines.AddRange(Decided(_decisions));
			lines.AddRange(Lines.Skip(1));
			return lines;
		}

		/// <summary>The decisions alone, for a pass made after the flatfile's own.</summary>
		public IEnumerable<ImportWarning> Decisions() => Decided(_decisions);

		public List<string> FinishRoutine() => [.. Decided(_routine).Select(decided => decided.Text)];

		private static IEnumerable<ImportWarning> Decided(
			SortedDictionary<string, (ImportWarningKind Kind, bool NeedsAction, List<int> Dbrefs)> decisions)
			=> decisions.Select(decision =>
			{
				var (text, (kind, needsAction, dbrefs)) = decision;
				var sample = string.Join(" ", dbrefs.Take(10).Select(d => $"#{d}"));
				return new ImportWarning(kind,
					$"{text} ({dbrefs.Count} object(s): {sample}{(dbrefs.Count > 10 ? " ..." : string.Empty)})", needsAction);
			});
	}
}
