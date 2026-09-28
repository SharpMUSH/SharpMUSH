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
	private static SharpObject ObjectWith(SharpPower[] powers, SharpObjectFlag[] flags) =>
		new()
		{
			Key = 999,
			CreationTime = 0L,
			Name = "CaseSubject",
			Type = "Thing",
			Locks = ImmutableDictionary<string, SharpLockData>.Empty,
			Owner = new(async _ => { await ValueTask.CompletedTask; return null!; }),
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
			Alias = alias,
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
}
