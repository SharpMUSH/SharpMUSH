using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// RhostMUSH's <c>list(&lt;list&gt;, &lt;eval&gt;[, &lt;delim&gt;][, &lt;header&gt;][, &lt;target&gt;])</c> under
/// <c>rhost_list</c>, whose first three arguments are TinyMUX's list(). Every expectation is what a live
/// RhostMUSH 5.11.2 told for the same line: the header once before the results, nothing at all for an
/// empty list, and the target used only when the enactor controls it, the enactor otherwise. Each
/// receiver is a player of its own in a room of its own, and only lines carrying the test's marker count.
/// </summary>
public class RhostListFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	[Test]
	public async Task TheHeaderIsToldOnceBeforeTheResults()
	{
		var player = await PlayerInARoomOfItsOwnAsync("RhListHdr");
		var marker = TestIsolationHelpers.GenerateUniqueName("RhListHdr");

		await Assert.That(await EvalAsync(player.Handle, $"X[list(a b,{marker}##[ilev()]#@,,{marker}H)]Y", rhostList: true))
			.IsEqualTo("XY");

		await Assert.That(await HeardAsync(player.DbRef, marker, $"{marker}b02"))
			.IsEquivalentTo([$"{marker}H", $"{marker}a01", $"{marker}b02"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	[Arguments("")]
	[Arguments("%b%b")]
	public async Task AnEmptyListTellsNotEvenTheHeader(string list)
	{
		var player = await PlayerInARoomOfItsOwnAsync("RhListEmpty");
		var marker = TestIsolationHelpers.GenerateUniqueName("RhListEmpty");
		var before = Notifications.CountFor(player.DbRef);

		await Assert.That(await EvalAsync(player.Handle, $"X[list({list},{marker}x,,{marker}H)]Y", rhostList: true))
			.IsEqualTo("XY");
		await EvalAsync(player.Handle, $"pemit(me,{marker}done)", rhostList: true);

		await Assert.That(await HeardAsync(player.DbRef, marker, $"{marker}done", before))
			.IsEquivalentTo([$"{marker}done"]);
	}

	[Test]
	public async Task ATargetTheEnactorControlsHearsInsteadOfTheEnactor()
	{
		var target = await PlayerInARoomOfItsOwnAsync("RhListTgt");
		var marker = TestIsolationHelpers.GenerateUniqueName("RhListTgt");
		var god = WebAppFactoryArg.ExecutorDBRef;

		await EvalAsync(1, $"list(a b,{marker}%i0,,{marker}H,{target.DbRef})", rhostList: true);

		await Assert.That(await HeardAsync(target.DbRef, marker, $"{marker}b"))
			.IsEquivalentTo([$"{marker}H", $"{marker}a", $"{marker}b"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(Notifications.For(god).Any(line => line.Contains(marker, StringComparison.Ordinal))).IsFalse();
	}

	[Test]
	public async Task AThingTheEnactorOwnsIsATarget()
	{
		var player = await PlayerInARoomOfItsOwnAsync("RhListOwn");
		var marker = TestIsolationHelpers.GenerateUniqueName("RhListOwn");
		var created = await TestIsolationHelpers.CreateObjectCommandAsync(Parser, ConnectionService, $"{marker}Thing", player.Handle);
		var thingRef = DBRef.Parse(created.Message!.ToPlainText().Trim());

		await EvalAsync(player.Handle, $"list(a,{marker}%i0,,{marker}H,{thingRef})", rhostList: true);

		await Assert.That(await HeardAsync(thingRef, marker, $"{marker}a"))
			.IsEquivalentTo([$"{marker}H", $"{marker}a"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(Notifications.For(player.DbRef).Any(line => line.Contains(marker + "a", StringComparison.Ordinal)))
			.IsFalse();
	}

	[Test]
	public async Task ATargetTheEnactorDoesNotControlIsIgnored()
	{
		var player = await PlayerInARoomOfItsOwnAsync("RhListRef");
		var other = await PlayerInARoomOfItsOwnAsync("RhListOth");
		var marker = TestIsolationHelpers.GenerateUniqueName("RhListRef");

		await Assert.That(await EvalAsync(player.Handle, $"X[list(a b,{marker}%i0,,{marker}H,{other.DbRef})]Y", rhostList: true))
			.IsEqualTo("XY");

		await Assert.That(await HeardAsync(player.DbRef, marker, $"{marker}b"))
			.IsEquivalentTo([$"{marker}H", $"{marker}a", $"{marker}b"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(Notifications.For(other.DbRef).Any(line => line.Contains(marker, StringComparison.Ordinal))).IsFalse();
	}

	[Test]
	public async Task ATargetThatMatchesNothingIsIgnoredWithoutAWord()
	{
		var player = await PlayerInARoomOfItsOwnAsync("RhListBad");
		var marker = TestIsolationHelpers.GenerateUniqueName("RhListBad");
		var before = Notifications.CountFor(player.DbRef);

		await Assert.That(await EvalAsync(player.Handle, $"{marker}X[list(a,{marker}%i0,,{marker}H,NoSuch{marker})]Y", rhostList: true))
			.IsEqualTo($"{marker}XY");

		// Everything the player heard, think's own answer last: no locate error in between.
		await Notifications.WaitForAsync(player.DbRef, $"{marker}XY", startIndex: before);
		await Assert.That(Notifications.For(player.DbRef).Skip(before).ToList())
			.IsEquivalentTo([$"{marker}H", $"{marker}a", $"{marker}XY"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task TheThreeArgumentFormIsTinyMuxs()
	{
		var player = await PlayerInARoomOfItsOwnAsync("RhListMux");
		var marker = TestIsolationHelpers.GenerateUniqueName("RhListMux");

		await Assert.That(await EvalAsync(player.Handle,
				$"X[list(a b|c|d,{marker}%i0[if(strmatch(%i0,c),ibreak())],|)]Y", rhostList: true))
			.IsEqualTo("XY");

		await Assert.That(await HeardAsync(player.DbRef, marker, $"{marker}c"))
			.IsEquivalentTo([$"{marker}a b", $"{marker}c"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	[Arguments("list(a)", "#-1 FUNCTION (LIST) EXPECTS AT LEAST 2 ARGUMENTS BUT GOT 1")]
	[Arguments("list(a,b,c,d,e,f)", "#-1 FUNCTION (LIST) EXPECTS AT MOST 5 ARGUMENTS BUT GOT 6")]
	public async Task TheArgumentCountIsRhostMushs(string expression, string expected)
		=> await Assert.That(await EvalAsync(1, expression, rhostList: true)).IsEqualTo(expected);

	[Test]
	[Arguments("member(list(functions),LIST)", "0", false)]
	[Arguments("list(a b,x)", "#-1", true)]
	[Arguments("list(a b,x,,H)", "#-1 FUNCTION (LIST) EXPECTS AT MOST 2 ARGUMENTS BUT GOT 4", true)]
	public async Task OffListIsPennMushs(string expression, string expected, bool equal)
	{
		var result = await EvalAsync(1, expression, rhostList: false);
		if (equal)
		{
			await Assert.That(result).IsEqualTo(expected);
		}
		else
		{
			await Assert.That(result).IsNotEqualTo(expected);
		}
	}

	private async Task<string> EvalAsync(long handle, string expression, bool rhostList)
	{
		using var scope = TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { RhostList = rhostList }
		});
		var result = await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"think {expression}"));
		return result?.Message.ToPlainText() ?? string.Empty;
	}

	/// <summary>The marked lines <paramref name="who"/> heard, once <paramref name="last"/> has arrived.</summary>
	private async Task<List<string>> HeardAsync(DBRef who, string marker, string last, int startIndex = 0)
	{
		await Notifications.WaitForAsync(who, last, startIndex: startIndex);
		return Notifications.For(who).Skip(startIndex).Where(line => line.Contains(marker, StringComparison.Ordinal)).ToList();
	}

	private async Task<TestIsolationHelpers.TestPlayer> PlayerInARoomOfItsOwnAsync(string prefix)
	{
		var room = (await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"))).Message.ToPlainText()!.Trim();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {player.DbRef}={room}"));
		return player;
	}
}
