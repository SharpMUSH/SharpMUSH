using SharpMUSH.Library.Authorization;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using System.Collections.Immutable;

namespace SharpMUSH.Tests.Internal;

/// <summary>
/// Power and flag names are matched case-insensitively.
///
/// <para>PennMUSH resolves both through the same prefix table: <c>match_power</c> and
/// <c>match_flag</c> (<c>src/flags.c:124-149</c>) call <c>match_flag_ns</c>, which is
/// <c>ptab_find(n-&gt;tab, name)</c>. <c>ptab_find</c> (<c>src/ptab.c</c>) compares with
/// <c>strcasecmp</c> and <c>string_prefix</c>, and <c>string_prefix</c>
/// (<c>src/strutil.c</c>) compares through <c>DOWNCASE</c>. So <c>Can_Spoof</c>,
/// <c>can_spoof</c> and <c>CAN_SPOOF</c> all name the same power there.</para>
///
/// <para><c>ArgHelpers.HasObjectPowers</c> / <c>HasObjectFlags</c> once answered the same question
/// with an ordinal <c>==</c> (and, for flags, by reference), so a permission check depended on which
/// helper the call site reached for. Those forwarders are gone; every caller asks
/// <see cref="HelperFunctions.HasPower(SharpObject,string)"/> and
/// <see cref="HelperFunctions.HasFlag(SharpObject,string)"/> directly.</para>
/// </summary>
public class FlagPowerHelperCaseTests
{
	private static SharpObject ObjectWith(SharpPower[] powers, SharpObjectFlag[] flags, int key = 999) =>
		new()
		{
			Key = key,
			CreationTime = 0L,
			Name = "CaseSubject",
			Type = "Thing",
			Locks = ImmutableDictionary<string, SharpLockData>.Empty,
			Owner = new(async _ => { await ValueTask.CompletedTask; return null!; }),
			// A built-in power is a permission the object holds; any other is stored.
			Grants = TestHelpers.GrantsFor(key, false, powers: powers.Select(p => p.Name).ToArray()),
			Powers = new(() => powers.ToAsyncEnumerable()),
			Attributes = new(AsyncEnumerable.Empty<SharpAttribute>),
			LazyAttributes = new(AsyncEnumerable.Empty<LazySharpAttribute>),
			AllAttributes = new(AsyncEnumerable.Empty<SharpAttribute>),
			LazyAllAttributes = new(AsyncEnumerable.Empty<LazySharpAttribute>),
			Flags = new(() => flags.ToAsyncEnumerable()),
			Parent = new(async _ => { await ValueTask.CompletedTask; return new AnyOptionalSharpObject(new None()); }),
			Zone = new(async _ => { await ValueTask.CompletedTask; return new AnyOptionalSharpObject(new None()); }),
			Children = new(AsyncEnumerable.Empty<SharpObject>)
		};

	private static SharpPower Power(string name, string alias = "") =>
		new()
		{
			Name = name,
			Aliases = alias.Length == 0 ? [] : [alias],
			System = true,
			SetPermissions = [],
			UnsetPermissions = [],
			TypeRestrictions = []
		};

	private static SharpObjectFlag Flag(string name, string[]? aliases = null, string symbol = "S") =>
		new()
		{
			Name = name,
			Aliases = aliases,
			Symbol = symbol,
			System = true,
			SetPermissions = [],
			UnsetPermissions = [],
			TypeRestrictions = []
		};

	[Test]
	[Arguments("Can_Spoof")]
	[Arguments("can_spoof")]
	[Arguments("CAN_SPOOF")]
	[Arguments("cAn_SpOoF")]
	public async Task HasPower_MatchesTheNameRegardlessOfCase(string asked)
	{
		var obj = ObjectWith([Power("Can_Spoof")], []);

		await Assert.That(await obj.HasPower(asked))
			.IsTrue()
			.Because("PennMUSH's match_power resolves through ptab_find, which compares with strcasecmp");
	}

	[Test]
	[Arguments("Pueblo_Send")]
	[Arguments("pueblo_send")]
	[Arguments("PUEBLO_SEND")]
	public async Task HasPower_MatchesAnAliasRegardlessOfCase(string asked)
	{
		var obj = ObjectWith([Power("Send_OOB", "Pueblo_Send")], []);

		await Assert.That(await obj.HasPower(asked))
			.IsTrue()
			.Because("ptab_flag holds aliases alongside names, and the comparison is the same one");
	}

	[Test]
	public async Task HasPower_StillSaysNoToAPowerTheObjectLacks()
	{
		var obj = ObjectWith([Power("Can_Spoof")], []);

		await Assert.That(await obj.HasPower("Can_Dark")).IsFalse();
	}

	[Test]
	[Arguments("MONITOR")]
	[Arguments("monitor")]
	[Arguments("Monitor")]
	public async Task HasFlag_MatchesTheNameRegardlessOfCaseAndInstance(string asked)
	{
		var obj = ObjectWith([], [Flag("MONITOR", ["LISTENER", "WATCHER"])]);

		await Assert.That(await obj.HasFlag(asked))
			.IsTrue()
			.Because("match_flag resolves through the same case-insensitive ptab_find as match_power");
	}

	[Test]
	public async Task HasFlag_StillSaysNoToAFlagTheObjectLacks()
	{
		var obj = ObjectWith([], [Flag("MONITOR", ["LISTENER", "WATCHER"])]);

		await Assert.That(await obj.HasFlag("DARK")).IsFalse();
	}

	/// <summary>
	/// <c>flag_hash_lookup</c> (<c>src/flags.c:162-189</c>) falls back to the flag letter for a single
	/// character, and <c>letter_to_flagptr</c> compares it exactly: <c>h</c> is HALT, <c>H</c> is HAVEN.
	/// </summary>
	[Test]
	[Arguments("HALT", true)]
	[Arguments("halt", true)]
	[Arguments("h", true)]
	[Arguments("H", false)]
	[Arguments("COLOUR", true)]
	[Arguments("C", true)]
	[Arguments("c", false)]
	[Arguments("HAVEN", false)]
	public async Task HasFlagOrLetter_MatchesNameAliasOrExactLetter(string asked, bool expected)
	{
		var obj = ObjectWith([], [Flag("HALT", symbol: "h"), Flag("COLOR", ["COLOUR"], "C")]);

		await Assert.That(await obj.HasFlagOrLetter(asked)).IsEqualTo(expected);
	}

	/// <summary>
	/// <see cref="ObjectFlagSet"/> answers every question the per-read helpers answer, the same way: the
	/// set is read once and asked many times, so it must not drift from the helpers it replaces.
	/// </summary>
	[Test]
	[Arguments("HALT")]
	[Arguments("halt")]
	[Arguments("h")]
	[Arguments("H")]
	[Arguments("COLOUR")]
	[Arguments("colour")]
	[Arguments("COLOR")]
	[Arguments("C")]
	[Arguments("c")]
	[Arguments("HAVEN")]
	[Arguments("LISTENER")]
	[Arguments("watcher")]
	[Arguments("")]
	public async Task ObjectFlagSet_AgreesWithHasFlagAndHasFlagOrLetter(string asked)
	{
		var obj = ObjectWith([], [Flag("HALT", symbol: "h"), Flag("COLOR", ["COLOUR"], "C"),
			Flag("MONITOR", ["LISTENER", "WATCHER"], "M")]);
		var set = await obj.ReadFlagsAsync(CancellationToken.None);

		await Assert.That(set.Has(asked)).IsEqualTo(await obj.HasFlag(asked));
		await Assert.That(set.HasOrLetter(asked)).IsEqualTo(await obj.HasFlagOrLetter(asked));
	}

	/// <summary>
	/// <c>IsWizard</c>, <c>IsRoyalty</c> and <c>IsMistrust</c> match the flag's name only, not its
	/// aliases, while <c>Trust</c> goes through <c>HasFlag</c> and so answers to its alias too.
	/// </summary>
	[Test]
	[Arguments(new[] { "WIZARD" }, new string[0], 999)]
	[Arguments(new[] { "wizard" }, new string[0], 999)]
	[Arguments(new[] { "ROYALTY" }, new string[0], 999)]
	[Arguments(new[] { "MISTRUST" }, new string[0], 999)]
	[Arguments(new[] { "OTHER" }, new[] { "WIZARD", "ROYALTY", "MISTRUST" }, 999)]
	[Arguments(new[] { "TRUST" }, new[] { "INHERIT" }, 999)]
	[Arguments(new[] { "OTHER" }, new[] { "TRUST" }, 999)]
	[Arguments(new string[0], new string[0], 1)]
	[Arguments(new string[0], new string[0], 999)]
	public async Task ObjectFlagSet_PrivilegePredicatesAgreeWithTheHelpers(string[] names, string[] aliases, int key)
	{
		var flags = names.Select((name, i) => Flag(name, aliases, ((char)('a' + i)).ToString())).ToArray();
		var raw = ObjectWith([], flags, key);
		var obj = new AnySharpObject(new SharpThing
		{
			Object = raw,
			Location = new(async _ => { await ValueTask.CompletedTask; return null!; }),
			Home = new(async _ => { await ValueTask.CompletedTask; return null!; })
		});
		var set = await obj.ReadFlagsAsync(CancellationToken.None);

		await Assert.That(set.IsGod).IsEqualTo(obj.IsGod());
		await Assert.That(set.IsWizard).IsEqualTo(await obj.IsWizard(CancellationToken.None));
		await Assert.That(set.IsRoyalty).IsEqualTo(await obj.IsRoyalty(CancellationToken.None));
		await Assert.That(set.IsPriv).IsEqualTo(await obj.IsPriv(CancellationToken.None));
		await Assert.That(set.IsMistrust).IsEqualTo(await obj.IsMistrust(CancellationToken.None));
		await Assert.That(set.IsTrust).IsEqualTo(await obj.HasFlag("Trust", CancellationToken.None));
	}
}
