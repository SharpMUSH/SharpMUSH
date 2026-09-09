using OneOf.Types;
using SharpMUSH.Implementation.Common;
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
/// <para><see cref="ArgHelpers.HasObjectPowers"/> answered the same question as
/// <see cref="HelperFunctions.HasPower(SharpObject,string)"/> with an ordinal <c>==</c>, so a
/// permission check succeeded or failed depending on which helper the call site reached for and on
/// the casing the caller happened to pass. Same for
/// <see cref="ArgHelpers.HasObjectFlags"/> against
/// <see cref="HelperFunctions.HasFlag(SharpObject,string)"/>, which additionally compared flags by
/// reference — <see cref="SharpObjectFlag"/> is a class with no equality override, so only the very
/// instance hanging off the object could ever match.</para>
/// </summary>
public class ArgHelperPowerFlagCaseTests
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

	private static SharpObjectFlag Flag(string name, string[]? aliases = null) =>
		new()
		{
			Name = name,
			Aliases = aliases,
			Symbol = "S",
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
	public async Task HasObjectPowers_MatchesTheNameRegardlessOfCase(string asked)
	{
		var obj = ObjectWith([Power("Can_Spoof")], []);

		await Assert.That(await ArgHelpers.HasObjectPowers(obj, asked))
			.IsTrue()
			.Because("PennMUSH's match_power resolves through ptab_find, which compares with strcasecmp");
	}

	[Test]
	[Arguments("Pueblo_Send")]
	[Arguments("pueblo_send")]
	[Arguments("PUEBLO_SEND")]
	public async Task HasObjectPowers_MatchesAnAliasRegardlessOfCase(string asked)
	{
		var obj = ObjectWith([Power("Send_OOB", "Pueblo_Send")], []);

		await Assert.That(await ArgHelpers.HasObjectPowers(obj, asked))
			.IsTrue()
			.Because("ptab_flag holds aliases alongside names, and the comparison is the same one");
	}

	[Test]
	public async Task HasObjectPowers_StillSaysNoToAPowerTheObjectLacks()
	{
		var obj = ObjectWith([Power("Can_Spoof")], []);

		await Assert.That(await ArgHelpers.HasObjectPowers(obj, "Can_Dark")).IsFalse();
	}

	[Test]
	[Arguments("MONITOR")]
	[Arguments("monitor")]
	[Arguments("Monitor")]
	public async Task HasObjectFlags_MatchesTheNameRegardlessOfCaseAndInstance(string asked)
	{
		var obj = ObjectWith([], [Flag("MONITOR", ["LISTENER", "WATCHER"])]);

		await Assert.That(await ArgHelpers.HasObjectFlags(obj, Flag(asked)))
			.IsTrue()
			.Because("match_flag resolves through the same case-insensitive ptab_find as match_power");
	}

	[Test]
	public async Task HasObjectFlags_StillSaysNoToAFlagTheObjectLacks()
	{
		var obj = ObjectWith([], [Flag("MONITOR", ["LISTENER", "WATCHER"])]);

		await Assert.That(await ArgHelpers.HasObjectFlags(obj, Flag("DARK"))).IsFalse();
	}

	/// <summary>
	/// The whole point of the fix: the two helpers now answer identically, whatever is asked.
	/// </summary>
	[Test]
	[Arguments("Can_Spoof")]
	[Arguments("can_spoof")]
	[Arguments("CAN_SPOOF")]
	[Arguments("Can_Dark")]
	public async Task TheTwoPowerHelpersAgree(string asked)
	{
		var obj = ObjectWith([Power("Can_Spoof")], []);

		await Assert.That(await ArgHelpers.HasObjectPowers(obj, asked))
			.IsEqualTo(await obj.HasPower(asked));
	}
}
