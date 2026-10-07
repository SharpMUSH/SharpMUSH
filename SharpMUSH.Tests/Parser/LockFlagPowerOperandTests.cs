using SharpMUSH.Library.Authorization;
using Mediator;
using NSubstitute;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Services.Interfaces;
using ZiggyCreatures.Caching.Fusion;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using System.Collections.Immutable;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// <c>FLAG^</c> and <c>POWER^</c> lock operands answer the same question as
/// <see cref="Library.HelperFunctions.HasFlag(SharpObject,string)"/> and
/// <see cref="Library.HelperFunctions.HasPower(SharpObject,string)"/>.
///
/// <para>PennMUSH evaluates both through <c>sees_flag</c> → <c>flag_hash_lookup</c>
/// (<c>src/boolexp.c:502-510</c>, <c>src/flags.c:162-189</c>): the name or alias through
/// <c>ptab_find</c> (<c>strcasecmp</c>), then — for a single character — the flag letter, which
/// <c>letter_to_flagptr</c> compares exactly (<c>f-&gt;letter == c</c>).</para>
///
/// <para>The lock visitor carried its own copies: it upper-cased the operand and compared with
/// <c>==</c>, so a mixed-case power (<c>Can_Dark</c>, <c>Long_Fingers</c>) or power alias could never
/// pass a <c>POWER^</c> lock, a flag alias (<c>COLOUR</c>) never passed <c>FLAG^</c>, and a
/// lower-case letter (<c>h</c> for HALT) was looked up as its upper-case neighbour.</para>
/// </summary>
public class LockFlagPowerOperandTests
{
	/// <summary>
	/// <c>FLAG^</c> and <c>POWER^</c> read only the unlocker, so the parser's services are never reached.
	/// </summary>
	private static readonly IBooleanExpressionParser BooleanParser = new BooleanExpressionParser(
		Substitute.For<ILockEvaluationServices>(), Substitute.For<IMediator>(), new FusionCache(new FusionCacheOptions()));

	private static AnySharpObject ThingWith(SharpPower[] powers, SharpObjectFlag[] flags)
	{
		var obj = new SharpObject
		{
			Key = 998,
			CreationTime = 0L,
			Name = "LockOperandSubject",
			Type = "Thing",
			Locks = ImmutableDictionary<string, SharpLockData>.Empty,
			Owner = new(async _ => { await ValueTask.CompletedTask; return null!; }),
			// A built-in power is a permission the object holds; any other is stored.
			Grants = TestHelpers.GrantsFor(998, false, powers: powers.Select(p => p.Name).ToArray()),
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

		return new AnySharpObject(new SharpThing
		{
			Object = obj,
			Location = new(async _ => { await ValueTask.CompletedTask; return null!; }),
			Home = new(async _ => { await ValueTask.CompletedTask; return null!; })
		});
	}

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

	private static SharpObjectFlag Flag(string name, string symbol, string[]? aliases = null) =>
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

	private static readonly SharpPower[] Powers = [Power("Can_Dark"), Power("Send_OOB", "Pueblo_Send")];

	private static readonly SharpObjectFlag[] Flags =
		[Flag("COLOR", "C", ["COLOUR"]), Flag("HALT", "h"), Flag("MONITOR", "M", ["LISTENER", "WATCHER"])];

	[Test]
	[Arguments("POWER^Can_Dark", true)]
	[Arguments("POWER^can_dark", true)]
	[Arguments("POWER^CAN_DARK", true)]
	[Arguments("POWER^Pueblo_Send", true)]
	[Arguments("POWER^pueblo_send", true)]
	[Arguments("POWER^Send_OOB", true)]
	[Arguments("POWER^Long_Fingers", false)]
	[Arguments("FLAG^COLOR", true)]
	[Arguments("FLAG^color", true)]
	[Arguments("FLAG^COLOUR", true)]
	[Arguments("FLAG^listener", true)]
	[Arguments("FLAG^h", true)]
	[Arguments("FLAG^H", false)]
	[Arguments("FLAG^DARK", false)]
	public async Task OperandMatchesAsPennMUSHDoes(string lockText, bool expected)
	{
		var subject = ThingWith(Powers, Flags);

		await Assert.That(await BooleanParser.Compile(lockText)(subject, subject)).IsEqualTo(expected);
	}
}
