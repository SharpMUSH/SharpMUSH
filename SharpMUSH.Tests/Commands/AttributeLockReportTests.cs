using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH's <c>do_atrlock</c> (<c>src/attrib.c:2466</c>) answers a change with one line, <c>Attribute locked.</c> or
/// <c>Attribute unlocked.</c>, and refuses one the player may not write with its own wording. It never prints
/// <c>@set</c>'s <c>obj/attr - LOCKED set.</c> report.
/// </summary>
public class AttributeLockReportTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	/// <summary>A connected mortal alone in a room of its own, so it hears only what its own commands produce.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> Mortal(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var room = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName(prefix + "Room"), god));
		return await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, room);
	}

	private async Task<IReadOnlyList<string>> Run(TestIsolationHelpers.TestPlayer who, string command)
	{
		var before = Notifications.For(who.DbRef).Count;
		await WebAppFactoryArg.CommandParserFor(who.DbRef, who.Handle)
			.CommandParse(who.Handle, ConnectionService, MarkupText.Plain(command));
		return [.. Notifications.For(who.DbRef).Skip(before)];
	}

	private async Task<string> Eval(TestIsolationHelpers.TestPlayer who, string expression)
		=> (await WebAppFactoryArg.CommandParserFor(who.DbRef, who.Handle)
			.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	[Test]
	public async ValueTask LockAndUnlockSayOneLineEach()
	{
		var player = await Mortal("AtrLockReport");
		await Run(player, "&LOCKREPORT me=value");

		await Assert.That(await Run(player, "@atrlock me/LOCKREPORT=on")).IsEquivalentTo(["Attribute locked."]);
		await Assert.That(await Eval(player, "atrlock(me/LOCKREPORT)")).IsEqualTo("1");

		await Assert.That(await Run(player, "@atrlock me/LOCKREPORT=off")).IsEquivalentTo(["Attribute unlocked."]);
		await Assert.That(await Eval(player, "atrlock(me/LOCKREPORT)")).IsEqualTo("0");
	}

	[Test]
	public async ValueTask TheFunctionSaysTheSameOneLine()
	{
		var player = await Mortal("AtrLockFnReport");
		await Run(player, "&LOCKFNREPORT me=value");

		var before = Notifications.For(player.DbRef).Count;
		await Assert.That(await Eval(player, "atrlock(me/LOCKFNREPORT,on)")).IsEqualTo(string.Empty);
		await Assert.That(Notifications.For(player.DbRef).Skip(before).ToList()).IsEquivalentTo(["Attribute locked."]);
	}

	[Test]
	public async ValueTask AnAttributeThePlayerMayNotWriteIsRefusedInItsOwnWords()
	{
		var player = await Mortal("AtrLockSafe");
		await Run(player, "&LOCKSAFE me=value");
		await Run(player, "@set me/LOCKSAFE=safe");

		await Assert.That(await Run(player, "@atrlock me/LOCKSAFE=on"))
			.IsEquivalentTo(["You need to be able to set the attribute to change its lock."]);
		await Assert.That(await Eval(player, "atrlock(me/LOCKSAFE)")).IsEqualTo("0");
	}
}
