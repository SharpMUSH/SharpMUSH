using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library;

/// <summary>
/// Outcome of <see cref="HelperFunctions.SafeToAddRelationship"/>: whether adding a parent/zone
/// relationship is safe and, if not, which of PennMUSH's two distinct <c>do_parent</c> guards it
/// would violate (<c>src/set.c:1432</c> self-reference vs. <c>:1477</c> a cycle reachable through
/// the existing chain) - the two produce different player-facing text and callers that show that
/// text need to tell them apart. <see cref="HelperFunctions.SafeToAddZone"/> collapses this back
/// to a single bool: <c>do_chzone</c> (<c>src/set.c:421-444</c>) has its own, differently-worded
/// self/cycle messages, so a zone caller reusing parent wording here would be wrong, not just
/// imprecise - see the zone note on <see cref="HelperFunctions.SafeToAddZone"/>.
/// </summary>
public enum RelationshipSafety
{
	Safe,
	SelfReference,
	Cycle
}

public static partial class HelperFunctions
{
	private static readonly Regex DatabaseReferenceRegex = DatabaseReference();
	private static readonly Regex DatabaseReferenceWithAttributeRegex = DatabaseReferenceWithAttribute();
	private static readonly Regex ObjectWithAttributeRegex = ObjectWithAttribute();
	private static readonly Regex OptionalDatabaseReferenceWithAttributeRegex = OptionalDatabaseReferenceWithAttribute();
	private static readonly Regex DatabaseReferenceWithOptionalAttributeRegex = DatabaseReferenceWithOptionalAttribute();
	private static readonly Regex AttributeNameValidationRegex = AttributeNameValidation();

	public static async ValueTask<AnySharpObject> GetGod(IMediator mediator)
		=> (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Known;

	/// <summary>
	/// PennMUSH: Wizard(x) = God(x) || has_wizard_flag(x)
	/// </summary>
	public static ValueTask<bool> IsWizard(this AnySharpObject obj)
		=> obj.IsWizard(ExecutionBudget.CurrentToken);

	public static async ValueTask<bool> IsWizard(this AnySharpObject obj, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return obj.IsGod() || await obj.Object().Flags.Value
			.AnyAsync(x => x.Name.Equals("WIZARD", StringComparison.OrdinalIgnoreCase), cancellationToken);
	}

	public static ValueTask<bool> IsRoyalty(this AnySharpObject obj)
		=> obj.IsRoyalty(ExecutionBudget.CurrentToken);

	public static async ValueTask<bool> IsRoyalty(this AnySharpObject obj, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return await obj.Object().Flags.Value
			.AnyAsync(x => x.Name.Equals("ROYALTY", StringComparison.OrdinalIgnoreCase), cancellationToken);
	}

	public static ValueTask<bool> IsMistrust(this AnySharpObject obj)
		=> obj.IsMistrust(ExecutionBudget.CurrentToken);

	public static async ValueTask<bool> IsMistrust(this AnySharpObject obj, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return await obj.Object().Flags.Value
			.AnyAsync(x => x.Name.Equals("MISTRUST", StringComparison.OrdinalIgnoreCase), cancellationToken);
	}

	public static bool IsGod(this AnySharpObject obj)
		=> obj.Object().Key == 1;

	public static ValueTask<bool> IsPriv(this AnySharpObject obj)
		=> obj.IsPriv(ExecutionBudget.CurrentToken);

	public static async ValueTask<bool> IsPriv(this AnySharpObject obj, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return IsGod(obj) || await obj.IsWizard(cancellationToken) || await obj.IsRoyalty(cancellationToken);
	}

	public static async ValueTask<bool> IsSee_All(this AnySharpObject obj)
		=> await IsPriv(obj) || await obj.HasPower("See_All");

	public static async ValueTask<bool> IsGuest(this AnySharpObject obj)
		=> await obj.HasPower("Guest");

	/// <summary>
	/// The one approval predicate: <b>royalty or above, or carrying the <c>APPROVED</c> flag</b> — and
	/// never a guest, whatever else is true of it.
	///
	/// <para>The engine ships the rule, not the policy: what earns a character its <c>APPROVED</c> flag is
	/// each game's decision, expressed by setting the flag. Softcode reaches this same method through the
	/// <c>isapproved()</c> function, so a game's <c>+</c>-verbs and the C# side cannot drift into two
	/// different answers.</para>
	/// </summary>
	public static async ValueTask<bool> IsApproved(this AnySharpObject obj)
		=> !await obj.IsGuest()
			&& (await obj.IsPriv() || await obj.HasFlag("APPROVED"));

	/// <summary>
	/// Evaluates an <c>@function/restrict</c> restriction string against <paramref name="executor"/>,
	/// returning whether the executor is PERMITTED to call the function.
	///
	/// <para>The restriction is a space-separated list of permission keywords, each optionally
	/// prefixed with <c>!</c> to negate it. Recognised keywords: <c>nobody</c> (never permitted),
	/// <c>god</c>, <c>wizard</c>, <c>royalty</c>, <c>admin</c> (wizard or royalty). A bare keyword
	/// requires the executor to satisfy it; a <c>!</c>-prefixed keyword forbids executors that
	/// satisfy it. All tokens must pass. An empty/whitespace restriction permits everyone.</para>
	/// </summary>
	public static async ValueTask<bool> SatisfiesFunctionRestriction(this AnySharpObject executor, string? restriction)
	{
		if (string.IsNullOrWhiteSpace(restriction))
		{
			return true;
		}

		foreach (var rawToken in restriction.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries))
		{
			var negate = rawToken.StartsWith('!');
			var keyword = (negate ? rawToken[1..] : rawToken).Trim().ToLowerInvariant();
			if (keyword.Length == 0)
			{
				continue;
			}

			var satisfies = keyword switch
			{
				"nobody" => false,
				"god" => executor.IsGod(),
				"wizard" => await executor.IsWizard(),
				"royalty" => await executor.IsRoyalty(),
				"admin" => await executor.IsWizard() || await executor.IsRoyalty(),
				// Unknown keywords are treated permissively (ignored) so that unsupported PennMUSH
				// restriction flags never silently lock everyone out of a function.
				_ => true
			};

			// "nobody" forbids everyone regardless of negation: !nobody would mean "permit everyone",
			// which is the default, so only the bare form is meaningful.
			if (keyword == "nobody")
			{
				if (!negate)
				{
					return false;
				}

				continue;
			}

			// Bare keyword: executor must satisfy it. Negated keyword: executor must NOT satisfy it.
			if (negate ? satisfies : !satisfies)
			{
				return false;
			}
		}

		return true;
	}

	// VISUAL, DARK, LIGHT, AUDIBLE, ORPHAN and PUPPET are flags in PennMUSH (hdrs/dbdefs.h:132-162,
	// each one a has_flag_by_name call) and are seeded as flags by both supported providers. They used to be
	// asked of Powers, a collection that has never held an entry by any of those names, so every one of
	// them answered false unconditionally — DARK objects were listed by look and WHO, VISUAL granted
	// nothing, IsAlive()'s puppet and audible terms never fired. See issue #796. Can_Dark, See_All,
	// Hide and Long_Fingers nearby really are powers and are left alone.
	public static async ValueTask<bool> IsVisual(this AnySharpObject obj)
		=> await obj.HasFlag("VISUAL");

	public static async ValueTask<bool> IsDark(this AnySharpObject obj)
		=> await obj.HasFlag("DARK");

	public static async ValueTask<bool> IsDark(this SharpObject obj)
		=> await obj.HasFlag("DARK");

	public static async ValueTask<bool> IsLight(this AnySharpObject obj)
		=> await obj.HasFlag("LIGHT");

	public static async ValueTask<bool> IsOpaque(this AnySharpObject obj)
		=> await obj.HasFlag("OPAQUE");

	public static async ValueTask<bool> IsTransparent(this AnySharpObject obj)
		=> await obj.HasFlag("TRANSPARENT");

	public static async ValueTask<bool> IsCloudy(this AnySharpObject obj)
		=> await obj.HasFlag("CLOUDY");

	public static async ValueTask<bool> IsDarkLegal(this AnySharpObject obj)
		=> await obj.IsDark() && (await obj.CanDark() || !await obj.IsAlive());

	public static async ValueTask<bool> IsAudible(this AnySharpObject obj)
		=> await obj.HasFlag("AUDIBLE");

	public static async ValueTask<bool> IsOrphan(this AnySharpObject obj)
		=> await obj.HasFlag("ORPHAN");

	public static async ValueTask<bool> IsListener(this AnySharpObject obj) => await obj.HasFlag("Monitor");


	public static async ValueTask<bool> IsAlive(this AnySharpObject obj)
		=> obj.IsPlayer
			 || await IsPuppet(obj)
			 || (await IsAudible(obj) && await (obj.Object().LazyAllAttributes.Value)
				 .AnyAsync(x => x.Name == "FORWARDLIST"));

	public static async ValueTask<bool> IsPuppet(this AnySharpObject obj)
		=> await obj.HasFlag("PUPPET");

	public static ValueTask<bool> HasPower(this AnySharpObject obj, string power)
		=> obj.Object().HasPower(power);

	public static ValueTask<bool> HasPower(this AnySharpObject obj, string power, CancellationToken cancellationToken)
		=> obj.Object().HasPower(power, cancellationToken);

	/// <summary>
	/// Power-read failures propagate instead of being reported as "no power", which
	/// could fail open when the caller is checking a restriction. Database streams use
	/// <c>FreshAsyncEnumerable</c> to isolate each reader's enumeration state. See issue #798.
	/// </summary>
	public static ValueTask<bool> HasPower(this SharpObject obj, string power)
		=> obj.HasPower(power, ExecutionBudget.CurrentToken);

	public static async ValueTask<bool> HasPower(this SharpObject obj, string power, CancellationToken cancellationToken)
		=> await obj.Powers.Value
			.AnyAsync(x => (x.Name?.Equals(power, StringComparison.InvariantCultureIgnoreCase) ?? false)
									 || (x.Alias?.Equals(power, StringComparison.InvariantCultureIgnoreCase) ?? false), cancellationToken);

	/// <summary>
	/// PennMUSH's <c>Hearer</c> (<c>src/game.c:1564</c>) walks <c>ATTR_FOR_EACH(thing, ptr)</c>,
	/// which expands to <c>for (var = List(obj); AL_NAME(var); var++)</c> (<c>hdrs/attrib.h:189</c>)
	/// — the object's own attribute list only, with no parent traversal. The FORWARDLIST and LISTEN
	/// lookups here are deliberately own-attribute-only (<c>parent: false</c>) to match: a child of a
	/// parent carrying <c>@listen</c> is not a hearer in PennMUSH.
	/// </summary>
	public static async ValueTask<bool> IsHearer(this AnySharpObject obj, IConnectionService connections,
		IAttributeService attributes)
	{
		if (await connections.IsConnected(obj) || await obj.IsPuppet())
		{
			return true;
		}

		if (await obj.IsAudible() &&
				(await attributes.GetAttributeAsync(obj, obj, "FORWARDLIST", IAttributeService.AttributeMode.Read, false))
				.IsAttribute)
		{
			return true;
		}

		if ((await attributes.GetAttributeAsync(obj, obj, "LISTEN", IAttributeService.AttributeMode.Read, false))
				.IsAttribute)
		{
			return true;
		}

		return false;
	}


	public static async ValueTask<bool> HasActiveCommands(this AnySharpObject obj, IAttributeService attributes)
	{
		if (await obj.HasFlag("NO_COMMAND")) return false;

		var attrs = await attributes.GetAttributePatternAsync(obj, obj, "*", true,
			IAttributeService.AttributePatternMode.Wildcard);
		if (!attrs.IsAttribute)
		{
			return false;
		}

		return attrs.AsAttributes
			.Any(x => x.IsCommand());
	}

	public static bool HasType(this AnySharpObject obj, string validType) =>
		validType switch
		{
			"PLAYER" => obj.IsPlayer,
			"THING" => obj.IsThing,
			"ROOM" => obj.IsRoom,
			"EXIT" => obj.IsExit,
			_ => true,
		};

	public static string TypeString(this AnySharpObject obj) =>
		obj switch
		{
			{ IsPlayer: true } => "PLAYER",
			{ IsThing: true } => "THING",
			{ IsRoom: true } => "ROOM",
			{ IsExit: true } => "EXIT",
			_ => "OBJECT"
		};

	public static async ValueTask<bool> HasLongFingers(this AnySharpObject obj)
		=> await obj.IsPriv() || await obj.HasPower("Long_Fingers");

	public static ValueTask<bool> HasFlag(this AnySharpObject obj, string flag)
		=> obj.Object().HasFlag(flag);

	public static ValueTask<bool> HasFlag(this AnySharpObject obj, string flag, CancellationToken cancellationToken)
		=> obj.Object().HasFlag(flag, cancellationToken);

	/// <summary>
	/// Name <b>or</b> alias, as PennMUSH's <c>has_flag_by_name</c> resolves it: the name goes through
	/// <c>flag_hash_lookup</c> → <c>match_flag_ns</c>, which searches <c>ptab_flag</c> — declared in
	/// <c>src/flags.c</c> as "Table of flags by name, inc. aliases".
	/// </summary>
	/// <remarks>
	/// This matched <c>Name</c> alone, leaving every aliased flag reachable by exactly one of its
	/// spellings — <c>COLOUR</c> did not answer for <c>COLOR</c>, nor <c>LISTENER</c> for
	/// <c>MONITOR</c> — while <c>HasPower</c> one screen up already matched a power's alias. See #834.
	/// <para>
	/// The database-level <c>HasFlag</c> predicate in
	/// <see cref="IObjectStore.GetFilteredObjectsAsync"/> is defined to agree with this helper and is
	/// pinned against it on both supported providers, so the two move together.
	/// </para>
	/// <para>
	/// Not ported from <c>flag_hash_lookup</c>: its single-character fallback to a flag's <em>letter</em>,
	/// which would make <c>HasFlag("D")</c> mean DARK. Letters are not unique in the seed (ABODE and
	/// ANSI share 'A'), Penn disambiguates by object type, and nothing here asks by letter.
	/// </para>
	/// </remarks>
	public static ValueTask<bool> HasFlag(this SharpObject obj, string flag)
		=> HasFlag(obj, flag, ExecutionBudget.CurrentToken);

	public static async ValueTask<bool> HasFlag(this SharpObject obj, string flag, CancellationToken cancellationToken)
		=> await obj.Flags.Value
			.AnyAsync(x => x.Name.Equals(flag, StringComparison.InvariantCultureIgnoreCase)
									 || (x.Aliases ?? []).Any(a => a.Equals(flag, StringComparison.InvariantCultureIgnoreCase)), cancellationToken);

	/// <summary>
	/// PennMUSH <c>LOUD</c> (hlp/pennflag.hlp:256): "LOUD objects bypass all speech, channel speech, and
	/// interaction @locks. This flag can only be set by royalty or wizards." Penn consults it at the call
	/// site rather than inside <c>Chan_Can_Speak</c> — see <c>src/extchat.c:1539</c>.
	/// </summary>
	public static async ValueTask<bool> IsLoud(this AnySharpObject obj)
		=> await obj.HasFlag("LOUD");

	public static async ValueTask<bool> CanDark(this AnySharpObject obj)
		=> await obj.HasPower("Can_Dark") || await obj.IsWizard();

	public static async ValueTask<bool> CanHide(this AnySharpObject obj)
		=> await obj.HasPower("Hide") || await obj.IsPriv();

	/// <summary>
	/// The configured type ancestor (ANCESTOR_ROOM/PLAYER/EXIT/THING) for this object's type, derived
	/// purely from configuration and the object's union type. This is the cheapest possible check —
	/// no database access, no flag/power lookup — so callers can short-circuit the whole ancestor
	/// fall-through before touching the DB when the ancestor is disabled (null / -1 in config).
	/// Note: this does NOT honor the per-object ORPHAN power; use <see cref="Ancestor"/> for the
	/// orphan-aware result.
	/// </summary>
	public static DBRef? TypeAncestor(this AnySharpObject obj,
		IOptionsWrapper<SharpMUSHOptions> configuration)
		=> obj.Match(
			_ => configuration.CurrentValue.Database.AncestorPlayer is null
				? null
				: new DBRef(Convert.ToInt32(configuration.CurrentValue.Database.AncestorPlayer)),
			_ => configuration.CurrentValue.Database.AncestorRoom is null
				? null
				: new DBRef(Convert.ToInt32(configuration.CurrentValue.Database.AncestorRoom)),
			_ => configuration.CurrentValue.Database.AncestorExit is null
				? null
				: new DBRef(Convert.ToInt32(configuration.CurrentValue.Database.AncestorExit)),
			_ => configuration.CurrentValue.Database.AncestorThing is null
				? (DBRef?)null
				: new DBRef(Convert.ToInt32(configuration.CurrentValue.Database.AncestorThing))
		);

	public static async ValueTask<DBRef?> Ancestor(this AnySharpObject obj,
		IOptionsWrapper<SharpMUSHOptions> configuration)
	{
		// Cheapest-first: resolve the configured type ancestor (no DB, no power check). When the
		// ancestor is disabled for this type there is nothing to inherit, so skip the ORPHAN power
		// lookup entirely — this keeps the hot path free of any I/O when ancestors are off.
		var typeAncestor = obj.TypeAncestor(configuration);
		if (typeAncestor is null)
		{
			return null;
		}

		return await HasFlag(obj.Object(), "ORPHAN", ExecutionBudget.CurrentToken) ? null : typeAncestor;
	}

	/// <summary>
	/// PennMUSH <c>dbdefs.h:219</c>:
	/// <code>#define Inheritable(x) (IsPlayer(x) || Inherit(x) || Inherit(Owner(x)) || Wizard(x))</code>
	/// where <c>Inherit(x)</c> is <c>has_flag_by_name(x, "TRUST", NOTYPE)</c> (<c>dbdefs.h:143</c>).
	/// Both TRUST tests go through <see cref="HasFlag(SharpObject,string)"/> so they ask the same
	/// question the same way: <c>has_flag_by_name</c> resolves via <c>match_flag</c> →
	/// <c>ptab_find</c>, which compares with <c>strcasecmp</c>/<c>string_prefix</c>, so the match is
	/// case-insensitive and alias-aware. An ordinal comparison here could never match the seeded
	/// flag, which is spelled <c>TRUST</c> with the alias <c>INHERIT</c> (<c>FlagSeed.cs:22</c>).
	/// </summary>
	public static ValueTask<bool> Inheritable(this AnySharpObject obj)
		=> obj.Inheritable(ExecutionBudget.CurrentToken);

	public static async ValueTask<bool> Inheritable(this AnySharpObject obj, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return obj.IsPlayer
			|| await obj.HasFlag("Trust", cancellationToken)
			|| await (await obj.Object().Owner.WithCancellation(cancellationToken))
				.Object.HasFlag("Trust", cancellationToken)
			|| await obj.IsWizard(cancellationToken);
	}

	public static ValueTask<bool> Owns(this AnySharpObject who, AnySharpObject what)
		=> who.Owns(what, ExecutionBudget.CurrentToken);

	public static async ValueTask<bool> Owns(this AnySharpObject who, AnySharpObject what, CancellationToken cancellationToken)
		=> (await who.Object().Owner.WithCancellation(cancellationToken)).Object.Id ==
			(await what.Object().Owner.WithCancellation(cancellationToken)).Object.Id;

	/// <summary>
	/// Takes the pattern of '#DBREF/attribute' and splits it out if possible.
	/// </summary>
	/// <param name="dbReferenceAttr">#DBREF/Attribute</param>
	/// <returns><see cref="DbRefAttribute"/> if it is a valid DbRef/Attribute format. Otherwise, <see cref="None"/>.</returns>
	public static Option<DbRefAttribute> SplitDBRefAndAttr(string dbReferenceAttr)
	{
		var match = DatabaseReferenceWithAttributeRegex.Match(dbReferenceAttr);
		var obj = match.Groups["Object"].Value;

		var attr = match.Groups["Attribute"].Value;
		if (!IsValidAttributeName(attr))
			return new None();

		return !string.IsNullOrEmpty(attr) && DBRef.TryParse(obj, out var dbRef)
				? new DbRefAttribute(dbRef!.Value, attr.ToUpper().Split('`'))
				: new None()
			;
	}

	/// <summary>
	/// Takes the pattern of 'Object/attribute' and splits it out if possible.
	/// </summary>
	/// <param name="objectAttr">Object/Attribute</param>
	/// <returns>The two halves if it is a valid Object/Attribute format. Otherwise, <see langword="null"/>.</returns>
	public static ObjectAttribute? SplitObjectAndAttr(string objectAttr)
	{
		var match = ObjectWithAttributeRegex.Match(objectAttr);
		var obj = match.Groups["Object"].Value;

		var attr = match.Groups["Attribute"].Value;
		if (!IsValidAttributeName(attr))
			return null;

		return string.IsNullOrEmpty(attr) || string.IsNullOrEmpty(obj)
			? null
			: new ObjectAttribute(obj, attr);
	}

	/// <summary>
	/// Takes the pattern of '[Object/]attribute' and splits it out if possible.
	/// </summary>
	/// <param name="ObjectAttr">[Object/]Attribute</param>
	/// <returns>The two halves if it is a valid [Object/]Attribute format. Otherwise, <see langword="null"/>.</returns>
	public static AttributeWithOptionalObject? SplitOptionalObjectAndAttr(string ObjectAttr)
	{
		var match = OptionalDatabaseReferenceWithAttributeRegex.Match(ObjectAttr);
		var obj = match.Groups["Object"].Value;

		var attr = match.Groups["Attribute"].Value;
		if (!IsValidAttributeName(attr))
			return null;

		return string.IsNullOrEmpty(attr)
			? null
			: new AttributeWithOptionalObject(string.IsNullOrEmpty(obj) ? null : obj, attr);
	}

	/// <summary>
	/// Detects self-reference and cycles when combining parent and zone chains. Checks whether
	/// adding a relationship would create a cycle by following both parent and zone links from the
	/// new relationship target.
	/// </summary>
	/// <param name="start">The object that will have a new relationship set</param>
	/// <param name="newRelated">The object being set as parent or zone</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>
	/// <see cref="RelationshipSafety.Safe"/> if adding the relationship is safe;
	/// <see cref="RelationshipSafety.SelfReference"/> if <paramref name="start"/> and
	/// <paramref name="newRelated"/> are the same object; <see cref="RelationshipSafety.Cycle"/> if
	/// <paramref name="start"/> is otherwise reachable from <paramref name="newRelated"/>.
	/// </returns>
	public static async ValueTask<RelationshipSafety> SafeToAddRelationship(IMediator mediator, IObjectStore database, AnySharpObject start, AnySharpObject newRelated, CancellationToken cancellationToken = default)
	{
		var startDbRef = start.Object().DBRef;
		var newRelatedDbRef = newRelated.Object().DBRef;

		if (startDbRef.Number == newRelatedDbRef.Number)
		{
			return RelationshipSafety.SelfReference;
		}

		// If start is reachable FROM newRelated via parent/zone edges, then adding the relationship
		// would complete a cycle: start -> newRelated -> ... -> start
		var isReachable = await database.IsReachableViaParentOrZoneAsync(newRelated, start, cancellationToken: cancellationToken);

		return isReachable ? RelationshipSafety.Cycle : RelationshipSafety.Safe;
	}

	/// <summary>
	/// Detects self-reference and cycles in the parent chain. Distinguishes the two
	/// (<see cref="RelationshipSafety"/>) because PennMUSH's <c>do_parent</c> notifies the player
	/// with different text for each (<c>src/set.c:1432,1477</c>).
	/// </summary>
	public static async ValueTask<RelationshipSafety> SafeToAddParent(IMediator mediator, IObjectStore database, AnySharpObject start, AnySharpObject newParent, CancellationToken cancellationToken = default)
		=> await SafeToAddRelationship(mediator, database, start, newParent, cancellationToken);

	/// <summary>
	/// Detects cycles in the zone chain. Collapsed to a bool - unlike <see cref="SafeToAddParent"/>,
	/// no caller here needs to tell self-reference from a cycle apart: PennMUSH's <c>do_chzone</c>
	/// (<c>src/set.c:421-444</c>) has its own self ("You shouldn't zone objects to themselves!") and
	/// cycle ("You can't make circular zones!") messages, both worded differently from
	/// <c>do_parent</c>'s and neither currently reproduced here, so there is nothing parent-specific
	/// to route to. If zone messaging is split to match Penn later, wire it from
	/// <see cref="SafeToAddRelationship"/> directly rather than reusing the parent-flavoured keys.
	/// </summary>
	public static async ValueTask<bool> SafeToAddZone(IMediator mediator, IObjectStore database, AnySharpObject start, AnySharpObject newZone, CancellationToken cancellationToken = default)
		=> await SafeToAddRelationship(mediator, database, start, newZone, cancellationToken) == RelationshipSafety.Safe;

	/// <summary>
	/// Takes the pattern of 'Object[/attribute]' and splits it out if possible.
	/// </summary>
	/// <param name="DBRefAttr">Object[/Attribute]</param>
	/// <returns>The two halves if it is a valid Object[/Attribute] format. Otherwise, <see langword="null"/>.</returns>
	public static ObjectWithOptionalAttribute? SplitDbRefAndOptionalAttr(string DBRefAttr)
	{
		var match = DatabaseReferenceWithOptionalAttributeRegex.Match(DBRefAttr);
		var obj = match.Groups["Object"].Value;

		var attr = match.Groups["Attribute"].Value;
		if (!string.IsNullOrEmpty(attr) && !IsValidAttributeName(attr))
			return null;

		return string.IsNullOrEmpty(obj)
			? null
			: new ObjectWithOptionalAttribute(obj, string.IsNullOrEmpty(attr) ? null : attr);
	}

	public static Option<DBRef> ParseDbRef(string dbrefStr)
	{
		var match = DatabaseReferenceRegex.Match(dbrefStr);
		var dbref = match.Groups["DatabaseNumber"].Value;
		var cTime = match.Groups["CreationTimestamp"].Value;

		return string.IsNullOrEmpty(dbref)
			? new None()
			: new DBRef(int.Parse(dbref), string.IsNullOrWhiteSpace(cTime) ? null : long.Parse(cTime));
	}

	/// <summary>
	/// A regular expression that takes the form of '#123:43143124' or '#543'.
	/// </summary>
	/// <returns>A regex that has a named group for the DBRef Number and Creation Milliseconds.</returns>
	[GeneratedRegex(@"^#(?<DatabaseNumber>\d+)(?::(?<CreationTimestamp>\d+))?$")]
	private static partial Regex DatabaseReference();

	/// <summary>
	/// A regular expression that takes the form of 'Object/attributeName'.
	/// </summary>
	/// <returns>A regex that has a named group for the Object and Attribute.</returns>
	[GeneratedRegex(@"^(?<Object>#\d+(?::\d+)?)/(?<Attribute>[a-zA-Z0-9#@_\-\.`\?\*\[\]\(\)\+\<\>\^\$]+)$")]
	private static partial Regex DatabaseReferenceWithAttribute();

	/// <summary>
	/// A regular expression that takes the form of 'Object/attributeName'. The attribute half
	/// accepts wildcard and regex metacharacters as literals, so one pattern covers every caller;
	/// which of those the characters actually mean is decided later, by the matching mode the
	/// caller asks for. '#' is allowed because PennMUSH permits it in attribute names
	/// (e.g. bb_post_bdy_#1, produced by &amp; attr_%# obj=value patterns).
	/// </summary>
	/// <returns>A regex that has a named group for the Object and Attribute.</returns>
	[GeneratedRegex(@"^(?<Object>[^/]+)/(?<Attribute>[a-zA-Z0-9@_\-\.`\?\*\[\]\(\)\+\<\>\^\$#]+)$")]
	private static partial Regex ObjectWithAttribute();

	/// <summary>
	/// A regular expression that takes the form of '[Object/]attributeName'.
	/// </summary>
	/// <returns>A regex that has a named group for the Object and Attribute.</returns>
	[GeneratedRegex(@"^(?:(?<Object>[^/]+)/)?(?<Attribute>[a-zA-Z0-9@_\-\.`\?\*\[\]\(\)\+\<\>\^\$#]+)$")]
	private static partial Regex OptionalDatabaseReferenceWithAttribute();

	/// <summary>
	/// A regular expression that takes the form of '[Object/]attributeName'.
	/// </summary>
	/// <returns>A regex that has a named group for the Object and Attribute.</returns>
	[GeneratedRegex(@"^(?<Object>[^/]+)(?:/(?<Attribute>[a-zA-Z0-9@_\-\.`\?\*\[\]\(\)\+\<\>\^\$#]+))?$")]
	private static partial Regex DatabaseReferenceWithOptionalAttribute();

	/// <summary>
	/// Validates basic attribute name format. Matches PennMUSH good_atr_name() which permits
	/// any printable character except backtick, pipe, semicolon, and braces.
	/// Includes # because attribute names set via &amp; attr_%# obj=val expand the dbref into the name.
	/// </summary>
	[GeneratedRegex(@"^[a-zA-Z0-9@_\-\.`\?\*\[\]\(\)\+\<\>\^\$#]+$")]
	private static partial Regex AttributeNameValidation();

	/// <summary>
	/// Validates that an attribute name is well-formed
	/// </summary>
	/// <param name="attributeName">The attribute name to validate</param>
	/// <returns>True if valid, false otherwise</returns>
	private static bool IsValidAttributeName(string attributeName)
	{
		if (string.IsNullOrEmpty(attributeName))
			return false;

		return AttributeNameValidationRegex.IsMatch(attributeName);
	}

	/// <summary>
	/// Returns <see langword="true"/> when the attribute specifier is an anonymous
	/// <c>#lambda/…</c> or <c>#apply[N]/…</c> expression rather than an
	/// <c>object/attribute</c> database reference.
	/// </summary>
	/// <param name="attributeSpecifier">The plain-text attribute specifier string.</param>
	public static bool IsLambdaOrApply(string attributeSpecifier)
		=> attributeSpecifier.StartsWith("#lambda", StringComparison.OrdinalIgnoreCase)
		|| attributeSpecifier.StartsWith("#apply", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Strips a single pair of outer braces from an <see cref="MString"/>, if present.
	/// This is the SharpMUSH equivalent of PennMUSH's <c>PE_COMMAND_BRACES</c> flag,
	/// which strips only the first (outermost) brace level at execution time.
	/// Used by command handlers whose arguments were preserved via
	/// <see cref="ParserInterfaces.ParserStateFlags.PreserveBraces"/> during argument parsing.
	/// </summary>
	public static MString StripOuterBraces(MString input)
	{
		var text = input.ToPlainText();
		if (text.Length >= 2 && text[0] == '{' && text[^1] == '}')
			return input.Substring(1, input.Length - 2);
		return input;
	}
}