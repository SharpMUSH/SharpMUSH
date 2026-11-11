using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// What <c>@destroy</c>, <c>@nuke</c> and <c>@undestroy</c> say, word for word as PennMUSH's
/// <c>do_destroy</c>, <c>pre_destroy</c>, <c>do_undestroy</c> and <c>undestroy</c> say it
/// (<c>src/destroy.c:327-590</c>), and the RQUOTA line examine counts for a player. The parity
/// harness runs the same in <c>44-objects-audit</c> (<c>obj.destroy-cycle</c>) and
/// <c>20-admin-commands</c> (<c>admin.wizard</c>).
/// <para>A wizard and an ordinary player each stand in a room of their own, so what they are told is
/// theirs alone. Nothing here is destroyed twice, and a purge frees a GOING object only on its second
/// pass, so a concurrent purge cannot free one between a test's @nuke and its @undestroy.</para>
/// </summary>
public class DestroyCycleMessageTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private TestIsolationHelpers.TestPlayer _wizard = null!;
	private TestIsolationHelpers.TestPlayer _mortal = null!;

	[Before(Test)]
	public async Task CreatePlayers()
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var wizardRoom = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName("DestroyMsgWizRoom"), god));
		var mortalRoom = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName("DestroyMsgMortalRoom"), god));
		_wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DestroyMsgWiz", wizardRoom);
		_mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DestroyMsgOwner", mortalRoom);
		var wizard = (await Mediator.Send(new GetObjectNodeQuery(_wizard.DbRef))).Expect<SharpPlayer>();
		var flag = await Mediator.Send(new GetObjectFlagQuery("WIZARD"));
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(wizard, flag!))).IsTrue();
	}

	[After(Test)]
	public async Task Disconnect()
	{
		if (_wizard is not null) await ConnectionService.Disconnect(_wizard.Handle);
		if (_mortal is not null) await ConnectionService.Disconnect(_mortal.Handle);
	}

	private async ValueTask<string> Run(TestIsolationHelpers.TestPlayer who, string command)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who.DbRef);
		await WebAppFactoryArg.CommandParserFor(who.DbRef, who.Handle)
			.CommandParse(who.Handle, ConnectionService, MarkupText.Plain(command));
		return Told(who, before);
	}

	private string Told(TestIsolationHelpers.TestPlayer who, int from = 0)
		=> string.Join("\n", WebAppFactoryArg.Notifications.For(who.DbRef).Skip(from));

	private async Task<(string Name, DBRef DbRef)> CreateAsync(TestIsolationHelpers.TestPlayer owner, string prefix)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		var result = await WebAppFactoryArg.CommandParserFor(owner.DbRef, owner.Handle)
			.CommandParse(owner.Handle, ConnectionService, MarkupText.Plain($"@create {name}"));
		return (name, DBRef.Parse(result.Message.ToPlainText().Trim()));
	}

	[Test]
	public async Task NukingSomeoneElsesThingTellsTheOwner_AndSoDoesSparingIt()
	{
		var (name, thing) = await CreateAsync(_mortal, "Wreck");
		var ownerHeard = WebAppFactoryArg.Notifications.CountFor(_mortal.DbRef);

		var scheduled = await Run(_wizard, $"@nuke {thing}");
		await Assert.That(scheduled).Contains($"{_mortal.Name}'s {name}(#{thing.Number}");
		await Assert.That(scheduled).EndsWith(") is scheduled to be destroyed.");
		var ownerTold = Told(_mortal, ownerHeard);
		await Assert.That(ownerTold).Contains($"{_wizard.Name} has scheduled your {name}(#{thing.Number}");
		await Assert.That(ownerTold).EndsWith(") for destruction.");

		ownerHeard = WebAppFactoryArg.Notifications.CountFor(_mortal.DbRef);
		var spared = await Run(_wizard, $"@undestroy {thing}");
		await Assert.That(spared).Contains($"{_mortal.Name}'s {name}(#{thing.Number}");
		await Assert.That(spared).EndsWith(") has been spared from destruction.");
		await Assert.That(Told(_mortal, ownerHeard)).Contains($"Your {name}(#{thing.Number}");
	}

	[Test]
	public async Task SparingOnesOwnThingSaysYour()
	{
		var (name, thing) = await CreateAsync(_mortal, "OwnWreck");
		await Run(_mortal, $"@destroy {thing}");

		var spared = await Run(_mortal, $"@undestroy {thing}");

		await Assert.That(spared).StartsWith($"Your {name}(#{thing.Number}");
		await Assert.That(spared).EndsWith(") has been spared from destruction.");
	}

	[Test]
	public async Task DestroyingOnesOwnRoomShakesIt_AndPromisesAReward()
	{
		var roomName = TestIsolationHelpers.GenerateUniqueName("WreckRoom");
		var room = DBRef.Parse((await WebAppFactoryArg.CommandParserFor(_wizard.DbRef, _wizard.Handle)
			.CommandParse(_wizard.Handle, ConnectionService, MarkupText.Plain($"@dig {roomName}"))).Message.ToPlainText().Trim());
		await Run(_wizard, $"@tel me={room}");

		var scheduled = await Run(_wizard, $"@destroy {room}");

		await Assert.That(scheduled).Contains("The room shakes and begins to crumble.");
		await Assert.That(scheduled).Contains($"You will be rewarded shortly for {roomName}(#{room.Number}");
	}

	[Test]
	public async Task DestroyingSomeoneElsesRoomSendsTheWreckingBall()
	{
		var roomName = TestIsolationHelpers.GenerateUniqueName("TheirRoom");
		var room = DBRef.Parse((await WebAppFactoryArg.CommandParserFor(_mortal.DbRef, _mortal.Handle)
			.CommandParse(_mortal.Handle, ConnectionService, MarkupText.Plain($"@dig {roomName}"))).Message.ToPlainText().Trim());
		var ownerHeard = WebAppFactoryArg.Notifications.CountFor(_mortal.DbRef);

		var scheduled = await Run(_wizard, $"@nuke {room}");

		await Assert.That(scheduled).Contains($"The wrecking ball is on its way for {_mortal.Name}'s {roomName}(#{room.Number}");
		await Assert.That(scheduled).EndsWith(") and its exits.");
		await Assert.That(Told(_mortal, ownerHeard)).Contains($"{_wizard.Name} has scheduled your room {roomName}(#{room.Number}");
	}

	[Test]
	public async Task ExamineCountsRemainingQuota_AndKeepsInStepWithBuilding()
	{
		var player = (await Mediator.Send(new GetObjectNodeQuery(_mortal.DbRef))).Expect<SharpPlayer>();
		var before = player.Quota - await Mediator.Send(new GetOwnedObjectCountQuery(player));

		await Assert.That(await Run(_wizard, $"examine {_mortal.DbRef}")).Contains($"RQUOTA [#1m+]: {before}");

		await CreateAsync(_mortal, "QuotaThing");

		await Assert.That(await Run(_wizard, $"examine {_mortal.DbRef}/RQUOTA")).IsEqualTo($"RQUOTA [#1m+]: {before - 1}");
	}

	[Test]
	public async Task RemainingQuotaIsMortalDark_AndNotStored()
	{
		await Assert.That(await Run(_mortal, "examine me")).DoesNotContain("RQUOTA");
		await Assert.That(await Run(_wizard, $"think hasattr({_mortal.DbRef},RQUOTA)")).IsEqualTo("0");
	}
}
