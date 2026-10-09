using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// What <c>@edit</c> tells its executor: PennMUSH's <c>do_edit</c> (<c>src/set.c:963</c>) and
/// <c>edit_helper</c> (<c>src/set.c:791</c>). Each case runs as a mortal in a room of its own and reads what
/// that mortal heard. The texts were captured from PennMUSH 1.8.8 by <c>tools/parity/scenarios/70-edit.scn</c>.
/// </summary>
public class EditMessageTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix)
	{
		var roomName = TestIsolationHelpers.GenerateUniqueName($"{prefix}Room");
		var digResult = await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var room = DBRef.Parse(digResult.Message.ToPlainText()!.Trim());

		return await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, room);
	}

	private async Task<CallState> Run(TestIsolationHelpers.TestPlayer who, string command)
		=> await WebAppFactoryArg.CommandParserFor(who.DbRef, who.Handle)
			.CommandParse(who.Handle, ConnectionService, MarkupText.Plain(command));

	private async Task<List<string>> Heard(TestIsolationHelpers.TestPlayer who, string command)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who.DbRef);
		await Run(who, command);
		return [.. WebAppFactoryArg.Notifications.For(who.DbRef).Skip(before)];
	}

	/// <summary>A thing the player owns, carrying <c>ALPHA=foo bar foo</c> and <c>BETA=nothing here</c>.</summary>
	private async Task<string> Thing(TestIsolationHelpers.TestPlayer who)
	{
		var name = TestIsolationHelpers.GenerateUniqueName("EditThing");
		var created = await Run(who, $"@create {name}");
		var thing = created.Message.ToPlainText().Trim();
		await Run(who, $"&ALPHA {thing}=foo bar foo");
		await Run(who, $"&BETA {thing}=nothing here");
		return thing;
	}

	/// <summary>
	/// <c>src/set.c:936</c> — each edited attribute is reported with its new value, and one the search did not
	/// match with <c>- Unchanged.</c>; no count follows. Captured: <c>ALPHA - Set: baz bar baz</c>,
	/// <c>BETA - Unchanged.</c>
	/// </summary>
	[Test]
	public async ValueTask EditReportsEachAttributeWithItsNewValue()
	{
		var player = await Player("EdMsgSet");
		var thing = await Thing(player);

		var heard = await Heard(player, $"@edit {thing}/*=foo,baz");

		await Assert.That(heard).IsEquivalentTo(["ALPHA - Set: baz bar baz", "BETA - Unchanged."]);
	}

	/// <summary>
	/// <c>src/set.c:943</c> — <c>/check</c> prints the same <c>- Set:</c> line and changes nothing.
	/// </summary>
	[Test]
	public async ValueTask CheckShowsTheSetLineWithoutSetting()
	{
		var player = await Player("EdMsgChk");
		var thing = await Thing(player);

		var heard = await Heard(player, $"@edit/check {thing}/ALPHA=foo,baz");
		var value = await Heard(player, $"think get({thing}/ALPHA)");

		await Assert.That(heard).IsEquivalentTo(["ALPHA - Set: baz bar baz"]);
		await Assert.That(value).IsEquivalentTo(["foo bar foo"]);
	}

	/// <summary>
	/// <c>src/set.c:1003</c> — <c>/quiet</c> drops the per-attribute lines and ends with a count of edited
	/// and skipped attributes. Captured: <c>1 attributes edited, 1 skipped.</c>
	/// </summary>
	[Test]
	public async ValueTask QuietEndsWithACount()
	{
		var player = await Player("EdMsgQuiet");
		var thing = await Thing(player);

		var heard = await Heard(player, $"@edit/quiet {thing}/*=foo,baz");

		await Assert.That(heard).IsEquivalentTo(["1 attributes edited, 1 skipped."]);
	}

	/// <summary>
	/// <c>src/set.c:917</c> — a match counts as an edit even when the replacement leaves the value as it was.
	/// </summary>
	[Test]
	public async ValueTask AMatchThatChangesNothingIsStillSet()
	{
		var player = await Player("EdMsgSame");
		var thing = await Thing(player);

		var heard = await Heard(player, $"@edit {thing}/ALPHA=foo,foo");

		await Assert.That(heard).IsEquivalentTo(["ALPHA - Set: foo bar foo"]);
	}

	/// <summary>
	/// <c>do_edit</c>'s refusals: <c>No matching attributes.</c> (<c>src/set.c:1001</c>), <c>Nothing to do.</c>
	/// for an empty search (<c>src/set.c:985</c>) and <c>I need to know what you want to edit.</c> without a
	/// slash (<c>src/set.c:977</c>).
	/// </summary>
	[Test]
	public async ValueTask RefusalsUsePennMUSHsWording()
	{
		var player = await Player("EdMsgNo");
		var thing = await Thing(player);

		await Assert.That(await Heard(player, $"@edit {thing}/NOSUCHEDITATTR=foo,baz"))
			.IsEquivalentTo(["No matching attributes."]);
		await Assert.That(await Heard(player, $"@edit {thing}/ALPHA=,baz"))
			.IsEquivalentTo(["Nothing to do."]);
		await Assert.That(await Heard(player, $"@edit {thing}=foo,baz"))
			.IsEquivalentTo(["I need to know what you want to edit."]);
	}
}
