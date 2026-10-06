using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library;

/// <summary>
/// Outcome of <see cref="Services.Interfaces.IRelationshipCycleChecker"/>: whether adding a parent or
/// zone relationship is safe and, if not, which guard it would violate — a self-reference or a cycle
/// reachable through the existing chain. Both <c>do_parent</c> (<c>src/set.c:1432</c>, <c>:1477</c>)
/// and <c>do_chzone</c> (<c>src/set.c:421-444</c>) word the two differently, so callers that report
/// the refusal need to tell them apart.
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
		=> await mediator.Send(new GetObjectNodeQuery(new DBRef(1))) is AnySharpObject god
			? god
			: throw new InvalidOperationException("God (#1) does not exist.");

	/// <summary>The object's grants: its roles, overrides and the scopes they resolve to.</summary>
	public static Task<ObjectGrants> GrantsAsync(this AnySharpObject obj, CancellationToken cancellationToken)
		=> obj.Object().Grants.WithCancellation(cancellationToken);

	/// <summary>
	/// PennMUSH: Wizard(x) = God(x) || has_wizard_flag(x). The WIZARD flag is the <c>wizard</c> role, so
	/// this asks for <see cref="PortalPermission.GameWizard"/>, which any role may allow.
	/// </summary>
	public static ValueTask<bool> IsWizard(this AnySharpObject obj)
		=> obj.IsWizard(ExecutionBudget.CurrentToken);

	public static async ValueTask<bool> IsWizard(this AnySharpObject obj, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return obj.IsGod() || (await obj.GrantsAsync(cancellationToken)).Has(PortalPermission.GameWizard);
	}

	/// <summary>
	/// Whether the object holds <paramref name="scope"/>: God, or a role or override allowing it. The
	/// groups split out of WIZARD (<see cref="PortalPermission.ChatAdmin"/>,
	/// <see cref="PortalPermission.ServerOperate"/>, <see cref="PortalPermission.PlayersModerate"/>,
	/// <see cref="PortalPermission.ConfigAdmin"/>, ...) are asked through this, not <see cref="IsWizard(AnySharpObject)"/>.
	/// </summary>
	public static async ValueTask<bool> Can(this AnySharpObject obj, string scope)
		=> obj.IsGod() || (await obj.GrantsAsync(ExecutionBudget.CurrentToken)).Has(scope);

	public static ValueTask<bool> IsRoyalty(this AnySharpObject obj)
		=> obj.IsRoyalty(ExecutionBudget.CurrentToken);

	/// <summary>PennMUSH: Royalty(x) = has_flag(x, ROYALTY), the <c>royalty</c> role.</summary>
	public static async ValueTask<bool> IsRoyalty(this AnySharpObject obj, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return (await obj.GrantsAsync(cancellationToken)).Shows(PortalPermission.GameRoyalty);
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

	/// <summary><see cref="IsSee_All(AnySharpObject)"/> with the object's flags already read.</summary>
	public static async ValueTask<bool> IsSee_All(this AnySharpObject obj, ObjectFlagSet flags)
		=> flags.IsPriv || await obj.HasPower("See_All");

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
	/// <c>god</c>, <c>wizard</c>, <c>royalty</c>, <c>admin</c> (wizard or royalty), and
	/// <c>noguest</c>, <c>nogagged</c>, <c>nofixed</c> (not a guest; owner not GAGGED; owner not FIXED), and any
	/// permission scope (<c>players.moderate</c>: the executor holds it). A bare keyword
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
				// check_func (src/function.c): Guest() is the power, Gagged() and Fixed() the owner's flag.
				"noguest" => !await executor.IsGuest(),
				"nogagged" => !await OwnerHasFlag(executor, "GAGGED"),
				"nofixed" => !await OwnerHasFlag(executor, "FIXED"),
				// A permission, built in (players.moderate) or defined with @permission/define (scene.close): the
				// executor holds it. A dotted word nobody defined is held by God alone, so a typo fails closed.
				_ when keyword.Contains('.') => await executor.Can(keyword),
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

	private static async ValueTask<bool> OwnerHasFlag(AnySharpObject executor, string flag)
	{
		AnySharpObject owner = await executor.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken);
		return await owner.HasFlag(flag);
	}

	// VISUAL, DARK, LIGHT, AUDIBLE, ORPHAN and PUPPET are flags in PennMUSH (hdrs/dbdefs.h:132-162,
	// each one a has_flag_by_name call) and are seeded as flags by the provider. They used to be
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

	public static ValueTask<bool> IsDarkLegal(this AnySharpObject obj)
		=> obj.IsDarkLegal(ExecutionBudget.CurrentToken);

	public static async ValueTask<bool> IsDarkLegal(this AnySharpObject obj, CancellationToken cancellationToken)
		=> await obj.HasFlag("DARK", cancellationToken)
			&& (await obj.CanDark(cancellationToken) || !await obj.IsAlive(cancellationToken));

	public static async ValueTask<bool> IsAudible(this AnySharpObject obj)
		=> await obj.HasFlag("AUDIBLE");

	public static async ValueTask<bool> IsOrphan(this AnySharpObject obj)
		=> await obj.HasFlag("ORPHAN");

	public static async ValueTask<bool> IsListener(this AnySharpObject obj) => await obj.HasFlag("Monitor");


	public static ValueTask<bool> IsAlive(this AnySharpObject obj)
		=> obj.IsAlive(ExecutionBudget.CurrentToken);

	/// <summary>Players, puppets, and audible objects with a local root FORWARDLIST are alive.</summary>
	public static async ValueTask<bool> IsAlive(this AnySharpObject obj, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return obj.IsPlayer || await obj.HasFlag("PUPPET", cancellationToken)
			|| await obj.HasFlag("AUDIBLE", cancellationToken) && await obj.Object().LazyAttributes.Value
				.AnyAsync(attribute => attribute.Name.Equals("FORWARDLIST", StringComparison.OrdinalIgnoreCase), cancellationToken);
	}

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

	/// <remarks>
	/// A built-in power (<see cref="GamePowers"/>) is the object holding its scope through a role or an
	/// override; only a power added with <c>@power/add</c> is read from the object's stored powers.
	/// </remarks>
	public static async ValueTask<bool> HasPower(this SharpObject obj, string power, CancellationToken cancellationToken)
		=> GamePowers.Find(power) is { } gamePower
			? (await obj.Grants.WithCancellation(cancellationToken)).Shows(gamePower.Scope)
			: await obj.Powers.Value.AnyAsync(x => x.AnswersTo(power), cancellationToken);

	/// <summary>
	/// The powers the object shows, as <c>powers()</c> and examine list them: each built-in power whose
	/// scope a role or override allows it, then the powers stored on it.
	/// </summary>
	public static async ValueTask<IReadOnlyList<SharpPower>> ReadPowersAsync(this SharpObject obj, CancellationToken cancellationToken)
	{
		var grants = await obj.Grants.WithCancellation(cancellationToken);
		var stored = await obj.Powers.Value.Where(p => GamePowers.Find(p.Name) is null).ToListAsync(cancellationToken);
		return [.. GamePowers.All.Where(p => grants.Shows(p.Scope)).Select(PowerFor), .. stored];
	}

	public static ValueTask<IReadOnlyList<SharpPower>> ReadPowersAsync(this AnySharpObject obj)
		=> obj.Object().ReadPowersAsync(ExecutionBudget.CurrentToken);

	private static SharpPower PowerFor(GamePowers.Power power) => new()
	{
		Name = power.Name,
		Aliases = power.Aliases,
		Symbol = string.Empty,
		System = true,
		SetPermissions = [],
		UnsetPermissions = [],
		TypeRestrictions = []
	};

	/// <summary>
	/// The flags set on the object itself, by name: those stored on it and the role-backed ones
	/// (WIZARD, ROYALTY) it holds as its own roles, not through an account. What a package or a copy of
	/// the object carries.
	/// </summary>
	public static async ValueTask<IReadOnlyList<string>> ReadOwnFlagNamesAsync(this SharpObject obj, CancellationToken cancellationToken)
	{
		var grants = await obj.Grants.WithCancellation(cancellationToken);
		var stored = await obj.Flags.Value.Select(f => f.Name).ToListAsync(cancellationToken);
		return [.. stored, .. grants.Roles.Where(held => held.Source == RoleSource.Object)
			.Select(held => RoleFlags.ForRole(held.Role.Slug)?.Name).OfType<string>()];
	}

	/// <summary>
	/// The powers set on the object itself, by name: a built-in power's Allow override or role on the
	/// object, then the powers added with <c>@power/add</c> stored on it.
	/// </summary>
	public static async ValueTask<IReadOnlyList<string>> ReadOwnPowerNamesAsync(this SharpObject obj, CancellationToken cancellationToken)
	{
		var grants = await obj.Grants.WithCancellation(cancellationToken);
		var stored = await obj.Powers.Value.Select(p => p.Name).ToListAsync(cancellationToken);
		var own = GamePowers.All.Where(power => power.Role is { } role
			? grants.Roles.Any(held => held.Source == RoleSource.Object && held.Role.Slug == role)
			: PermissionResolver.StateOf(grants.Context.ObjectOverrides, power.Scope) == PermissionState.Allow);
		return [.. own.Select(power => power.Name), .. stored];
	}

	/// <summary>The role-backed flags (<see cref="RoleFlags"/>) a role or override shows on the object.</summary>
	public static IEnumerable<SharpObjectFlag> RoleFlagsShown(ObjectGrants grants)
		=> RoleFlags.All.Where(flag => grants.Shows(flag.Scope)).Select(flag => new SharpObjectFlag
		{
			Name = flag.Name,
			Symbol = flag.Symbol,
			System = true,
			SetPermissions = [],
			UnsetPermissions = [],
			TypeRestrictions = ["ROOM", "PLAYER", "EXIT", "THING"]
		});

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


	/// <summary>
	/// PennMUSH <c>Commer</c> (<c>src/game.c:1592-1601</c>), @sweep's "[commands]": any of the object's
	/// own attributes, at any depth of a tree, holds a <c>$</c>-command not set no_command. Parents are not
	/// consulted, and neither is the object's NO_COMMAND flag.
	/// </summary>
	public static async ValueTask<bool> HasActiveCommands(this AnySharpObject obj)
		=> await obj.Object().AllAttributes.Value.AnyAsync(x => x.IsCommand());

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
	/// pinned against it, so the two move together.
	/// </para>
	/// <para>
	/// Not ported from <c>flag_hash_lookup</c>: its single-character fallback to a flag's <em>letter</em>,
	/// which would make <c>HasFlag("D")</c> mean DARK. Letters are not unique in the seed (ABODE and
	/// ANSI share 'A'), Penn disambiguates by object type, and nothing here asks by letter.
	/// </para>
	/// </remarks>
	public static ValueTask<bool> HasFlag(this SharpObject obj, string flag)
		=> HasFlag(obj, flag, ExecutionBudget.CurrentToken);

	/// <para>
	/// WIZARD and ROYALTY are roles (<see cref="RoleFlags"/>): they answer from the object's grants.
	/// </para>
	public static async ValueTask<bool> HasFlag(this SharpObject obj, string flag, CancellationToken cancellationToken)
		=> RoleFlags.Find(flag) is { } roleFlag
			? (await obj.Grants.WithCancellation(cancellationToken)).Shows(roleFlag.Scope)
			: await obj.Flags.Value
				.AnyAsync(x => x.Name.Equals(flag, StringComparison.InvariantCultureIgnoreCase)
										 || (x.Aliases ?? []).Any(a => a.Equals(flag, StringComparison.InvariantCultureIgnoreCase)), cancellationToken);

	/// <summary>
	/// Reads the object's flags and grants once, for a caller that asks several flag questions about it,
	/// or lists the flags to show (the role-backed ones included). See <see cref="ObjectFlagSet"/>: each
	/// of its questions answers as the per-read helper here does.
	/// </summary>
	public static async ValueTask<ObjectFlagSet> ReadFlagsAsync(this SharpObject obj, CancellationToken cancellationToken)
		=> new(obj.DBRef, await obj.Flags.Value.ToListAsync(cancellationToken), await obj.Grants.WithCancellation(cancellationToken));

	public static ValueTask<ObjectFlagSet> ReadFlagsAsync(this SharpObject obj)
		=> obj.ReadFlagsAsync(ExecutionBudget.CurrentToken);

	public static ValueTask<ObjectFlagSet> ReadFlagsAsync(this AnySharpObject obj, CancellationToken cancellationToken)
		=> obj.Object().ReadFlagsAsync(cancellationToken);

	public static ValueTask<ObjectFlagSet> ReadFlagsAsync(this AnySharpObject obj)
		=> obj.Object().ReadFlagsAsync(ExecutionBudget.CurrentToken);

	/// <summary>
	/// <see cref="HasFlag(SharpObject,string)"/> plus the letter fallback of Penn's <c>flag_hash_lookup</c>
	/// (<c>src/flags.c:162-189</c>): a single character that names no flag is looked up as a flag
	/// letter, compared exactly (<c>letter_to_flagptr</c>: <c>f-&gt;letter == c</c>), so <c>h</c> is HALT
	/// and <c>H</c> is HAVEN. This is what <c>hasflag()</c> and a <c>FLAG^</c> lock ask.
	/// </summary>
	public static async ValueTask<bool> HasFlagOrLetter(this SharpObject obj, string nameOrLetter)
		=> await obj.HasFlag(nameOrLetter)
			|| (nameOrLetter.Length == 1
					&& (await obj.ReadFlagsAsync(ExecutionBudget.CurrentToken)).HasOrLetter(nameOrLetter));

	/// <summary>
	/// PennMUSH <c>LOUD</c> (hlp/pennflag.hlp:256): "LOUD objects bypass all speech, channel speech, and
	/// interaction @locks. This flag can only be set by royalty or wizards." Penn consults it at the call
	/// site rather than inside <c>Chan_Can_Speak</c> — see <c>src/extchat.c:1539</c>.
	/// </summary>
	public static async ValueTask<bool> IsLoud(this AnySharpObject obj)
		=> await obj.HasFlag("LOUD");

	public static ValueTask<bool> CanDark(this AnySharpObject obj)
		=> obj.CanDark(ExecutionBudget.CurrentToken);

	public static async ValueTask<bool> CanDark(this AnySharpObject obj, CancellationToken cancellationToken)
		=> await obj.HasPower("Can_Dark", cancellationToken) || await obj.IsWizard(cancellationToken);

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
	{
		var database = configuration.CurrentValue.Database;
		var ancestor = obj switch
		{
			SharpPlayer => database.AncestorPlayer,
			SharpRoom => database.AncestorRoom,
			SharpExit => database.AncestorExit,
			SharpThing => database.AncestorThing
		};

		return ancestor is null ? null : new DBRef(Convert.ToInt32(ancestor));
	}

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
		return obj.IsPlayer || await obj.Inheritable(await obj.ReadFlagsAsync(cancellationToken), cancellationToken);
	}

	/// <summary>
	/// <see cref="Inheritable(AnySharpObject, CancellationToken)"/> with the object's own flags already
	/// read; only the owner's TRUST flag is read here, and only when the object's own flags do not decide.
	/// </summary>
	public static async ValueTask<bool> Inheritable(this AnySharpObject obj, ObjectFlagSet flags,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return obj.IsPlayer
			|| flags.IsTrust
			|| flags.IsWizard
			|| await (await obj.Object().Owner.WithCancellation(cancellationToken))
				.Object.HasFlag("Trust", cancellationToken);
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
		if (!match.Success || !int.TryParse(match.Groups["DatabaseNumber"].ValueSpan, out var number))
			return new None();
		var timestamp = match.Groups["CreationTimestamp"];
		if (!timestamp.Success) return new DBRef(number);
		return long.TryParse(timestamp.ValueSpan, out var milliseconds)
			? new DBRef(number, milliseconds) : new None();
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
	[GeneratedRegex(@"^(?<Object>#\d+(?::\d+)?)/(?<Attribute>" + AttributePatternCharacters + "+)$")]
	private static partial Regex DatabaseReferenceWithAttribute();

	/// <summary>
	/// A regular expression that takes the form of 'Object/attributeName'. The attribute half
	/// accepts wildcard and regex metacharacters as literals, so one pattern covers every caller;
	/// which of those the characters actually mean is decided later, by the matching mode the
	/// caller asks for. '#' is allowed because PennMUSH permits it in attribute names
	/// (e.g. bb_post_bdy_#1, produced by &amp; attr_%# obj=value patterns).
	/// </summary>
	/// <returns>A regex that has a named group for the Object and Attribute.</returns>
	[GeneratedRegex(@"^(?<Object>[^/]+)/(?<Attribute>" + AttributePatternCharacters + "+)$")]
	private static partial Regex ObjectWithAttribute();

	/// <summary>
	/// A regular expression that takes the form of '[Object/]attributeName'.
	/// </summary>
	/// <returns>A regex that has a named group for the Object and Attribute.</returns>
	[GeneratedRegex(@"^(?:(?<Object>[^/]+)/)?(?<Attribute>" + AttributePatternCharacters + "+)$")]
	private static partial Regex OptionalDatabaseReferenceWithAttribute();

	/// <summary>
	/// A regular expression that takes the form of '[Object/]attributeName'.
	/// </summary>
	/// <returns>A regex that has a named group for the Object and Attribute.</returns>
	[GeneratedRegex(@"^(?<Object>[^/]+)(?:/(?<Attribute>" + AttributePatternCharacters + "+))?$")]
	private static partial Regex DatabaseReferenceWithOptionalAttribute();

	/// <summary>
	/// The characters an attribute NAME may contain: PennMUSH's <c>atr_name_table</c>
	/// (<c>utils/gentables.c</c>), which backs <c>good_atr_name</c>, <c>good_flag_name</c> and
	/// q-register and user-lock names. Penn upper-cases before it checks, so lower case is here too.
	/// A regex character class, for <see cref="Services.ValidateService"/>.
	/// </summary>
	public const string AttributeNameCharacters = @"[!""#$&'*+,\-./0-9;<=>?@A-Z_`|~a-z]";

	/// <summary>
	/// The characters the attribute half of <c>obj/attr</c> may contain. That half is a PATTERN for
	/// <c>@wipe</c>, <c>lattr()</c> and the rest, so this is every <see cref="AttributeNameCharacters"/>
	/// character plus the regex metacharacters <c>[ ] ( ) ^</c> (the wildcards <c>* ?</c> and <c>$</c> are
	/// already legal in a name). <c>/</c> is left out: it ends the object half, and every splitter here
	/// has always read <c>a/b/c</c> as no match rather than as attribute <c>b/c</c>.
	/// </summary>
	public const string AttributePatternCharacters = @"[!""#$&'*+,\-.0-9;<=>?@A-Z_`|~a-z\[\]()^]";

	[GeneratedRegex("^" + AttributePatternCharacters + "+$")]
	private static partial Regex AttributeNameValidation();

	/// <summary>
	/// Validates that the attribute half of <c>obj/attr</c> is a well-formed pattern.
	/// </summary>
	/// <param name="attributeName">The attribute name or pattern to validate</param>
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
