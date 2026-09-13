using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class FlagPermissionMetadataTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private readonly List<long> _handles = [];
	private readonly List<string> _flags = [];
	private readonly List<string> _powers = [];
	private async Task<TestIsolationHelpers.TestPlayer> Player()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "FlagMetadata");
		_handles.Add(player.Handle);
		return player;
	}
	private async Task<AnySharpObject> Node(DBRef dbref) => (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();
	private async Task<bool> Has(DBRef dbref, string flag) => await (await Node(dbref)).HasFlag(flag);
	private async Task Set(long handle, DBRef target, string flag, bool unset = false)
		=> _ = await Factory.CommandParser.CommandParse(handle, Connections, MarkupText.Plain($"@set {target}={(unset ? "!" : "")}{flag}"));
	private async Task<SharpObjectFlag> Flag(string[] set, string[] unset, string[]? types = null)
	{
		var name = "META" + Guid.NewGuid().ToString("N").ToUpperInvariant();
		_flags.Add(name);
		return (await Mediator.Send(new CreateObjectFlagCommand(name, [], "", false, set, unset, types ?? ["PLAYER"])))!;
	}
	[After(Test)]
	public async Task Cleanup()
	{
		foreach (var handle in _handles) await Connections.Disconnect(handle);
		foreach (var name in _flags) await Mediator.Send(new DeleteObjectFlagCommand(name));
		foreach (var name in _powers) await Mediator.Send(new DeletePowerCommand(name));
	}

	[Test]
	[Arguments("NOSPOOF", false, false)]
	[Arguments("NOSPOOF", false, true)]
	[Arguments("NOSPOOF", true, false)]
	[Arguments("NOSPOOF", true, true)]
	[Arguments("PARANOID", false, false)]
	[Arguments("PARANOID", false, true)]
	[Arguments("PARANOID", true, false)]
	[Arguments("PARANOID", true, true)]
	public async Task SeededVisibilityMetadataAllowsOwnerAndGod(string name, bool god, bool unset)
	{
		var owner = await Player();
		var definition = (await Mediator.Send(new GetObjectFlagQuery(name)))!;
		await Assert.That(definition.System).IsTrue();
		await Assert.That(definition.SetPermissions).Contains("odark");
		await Assert.That(definition.UnsetPermissions).Contains("odark");
		// Model an already assigned flag to test reset independently of set.
		if (unset) await Mediator.Send(new SetObjectFlagCommand(await Node(owner.DbRef), definition));
		await Set(god ? 1 : owner.Handle, owner.DbRef, name, unset);
		await Assert.That(await Has(owner.DbRef, name)).IsEqualTo(!unset);
		var stored = (await Mediator.Send(new GetObjectFlagQuery(name)))!;
		await Assert.That(stored.SetPermissions).IsEquivalentTo(definition.SetPermissions);
		await Assert.That(stored.UnsetPermissions).IsEquivalentTo(definition.UnsetPermissions);
	}

	[Test]
	[Arguments("DaRk")]
	[Arguments("mDaRk")]
	[Arguments("oDaRk")]
	[Arguments("LoG")]
	[Arguments("EvEnT")]
	public async Task MetadataOnlyAddsNoPrivilegeRequirement(string metadata)
	{
		var owner = await Player();
		var flag = await Flag([metadata], [metadata]);
		await Set(owner.Handle, owner.DbRef, flag.Name);
		await Assert.That(await Has(owner.DbRef, flag.Name)).IsTrue();
		await Set(owner.Handle, owner.DbRef, flag.Name, true);
		await Assert.That(await Has(owner.DbRef, flag.Name)).IsFalse();
	}

	[Test]
	[Arguments("InTeRnAl", false, false)]
	[Arguments("InTeRnAl", false, true)]
	[Arguments("InTeRnAl", true, false)]
	[Arguments("InTeRnAl", true, true)]
	[Arguments("DiSaBlEd", false, false)]
	[Arguments("DiSaBlEd", false, true)]
	[Arguments("DiSaBlEd", true, false)]
	[Arguments("DiSaBlEd", true, true)]
	public async Task ProhibitionsWinOverSatisfiedGodAlternative(string prohibition, bool unset, bool first)
	{
		var owner = await Player();
		string[] permissions = first ? [prohibition, "god"] : ["god", prohibition];
		var flag = await Flag(permissions, permissions);
		if (unset) await Mediator.Send(new SetObjectFlagCommand(await Node(owner.DbRef), flag));
		await Set(1, owner.DbRef, flag.Name, unset);
		await Assert.That(await Has(owner.DbRef, flag.Name)).IsEqualTo(unset);
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task DisabledDefinitionCannotBeSetOrResetEvenWithoutPrincipals(bool unset, bool metadata)
	{
		var owner = await Player();
		string[] permissions = metadata ? ["odark"] : [];
		var flag = await Flag(permissions, permissions);
		if (unset) await Mediator.Send(new SetObjectFlagCommand(await Node(owner.DbRef), flag));
		await Mediator.Send(new SetObjectFlagDisabledCommand(flag.Name, true));
		await Set(1, owner.DbRef, flag.Name, unset);
		await Assert.That(await Has(owner.DbRef, flag.Name)).IsEqualTo(unset);
		await Assert.That((await Mediator.Send(new GetObjectFlagQuery(flag.Name)))!.Disabled).IsTrue();
	}

	[Test]
	[Arguments("NOSPOOF", false)]
	[Arguments("NOSPOOF", true)]
	[Arguments("PARANOID", false)]
	[Arguments("PARANOID", true)]
	public async Task MetadataDoesNotBypassControl(string name, bool unset)
	{
		var owner = await Player();
		var stranger = await Player();
		if (unset) await Set(owner.Handle, owner.DbRef, name);
		await Set(stranger.Handle, owner.DbRef, name, unset);
		await Assert.That(await Has(owner.DbRef, name)).IsEqualTo(unset);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task MetadataDoesNotSatisfyWizardRequirement(bool unset)
	{
		var owner = await Player();
		var flag = await Flag(["odark", "WiZaRd", "event"], ["log", "WiZaRd", "dark"]);
		if (unset) await Set(1, owner.DbRef, flag.Name);
		await Set(owner.Handle, owner.DbRef, flag.Name, unset);
		await Assert.That(await Has(owner.DbRef, flag.Name)).IsEqualTo(unset);
		await Set(1, owner.DbRef, "WIZARD");
		await Set(owner.Handle, owner.DbRef, flag.Name, unset);
		await Assert.That(await Has(owner.DbRef, flag.Name)).IsEqualTo(!unset);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task CustomFlagOrPowerPrincipalRemainsAnAlternative(bool power)
	{
		var owner = await Player();
		var principal = await Flag([], []);
		var powerName = "METAPOWER" + Guid.NewGuid().ToString("N").ToUpperInvariant();
		_powers.Add(powerName);
		var powerDefinition = (await Mediator.Send(new CreatePowerCommand(powerName, "", "", false, [], [], ["PLAYER"])))!;
		var permissions = new[] { "odark", principal.Name.ToLowerInvariant(), powerName.ToLowerInvariant(), "event" };
		var flag = await Flag(permissions, permissions);
		await Set(owner.Handle, owner.DbRef, flag.Name);
		await Assert.That(await Has(owner.DbRef, flag.Name)).IsFalse();
		if (power) await Mediator.Send(new SetObjectPowerCommand(await Node(owner.DbRef), powerDefinition));
		else await Set(owner.Handle, owner.DbRef, principal.Name);
		await Set(owner.Handle, owner.DbRef, flag.Name);
		await Assert.That(await Has(owner.DbRef, flag.Name)).IsTrue();
		await Set(owner.Handle, owner.DbRef, flag.Name, true);
		await Assert.That(await Has(owner.DbRef, flag.Name)).IsFalse();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task SetAndResetUseIndependentPrincipalRequirements(bool restrictedSet)
	{
		var owner = await Player();
		var flag = await Flag(restrictedSet ? ["god", "event"] : ["odark"], restrictedSet ? ["odark"] : ["god", "event"]);
		await Set(owner.Handle, owner.DbRef, flag.Name);
		await Assert.That(await Has(owner.DbRef, flag.Name)).IsEqualTo(!restrictedSet);
		if (restrictedSet) await Set(1, owner.DbRef, flag.Name);
		await Set(owner.Handle, owner.DbRef, flag.Name, true);
		await Assert.That(await Has(owner.DbRef, flag.Name)).IsEqualTo(!restrictedSet);
		if (!restrictedSet) await Set(1, owner.DbRef, flag.Name, true);
		await Assert.That(await Has(owner.DbRef, flag.Name)).IsFalse();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OnlySelectedOperationProhibitionsApply(bool prohibitSet)
	{
		var owner = await Player();
		var flag = await Flag(prohibitSet ? ["internal"] : ["odark"], prohibitSet ? ["odark"] : ["disabled"]);
		if (prohibitSet) await Mediator.Send(new SetObjectFlagCommand(await Node(owner.DbRef), flag));
		await Set(owner.Handle, owner.DbRef, flag.Name, prohibitSet);
		await Assert.That(await Has(owner.DbRef, flag.Name)).IsEqualTo(!prohibitSet);
	}

	[Test]
	public async Task MetadataDoesNotBypassTypeRestriction()
	{
		var owner = await Player();
		var flag = await Flag(["odark"], ["odark"], ["THING"]);
		await Set(1, owner.DbRef, flag.Name);
		await Assert.That(await Has(owner.DbRef, flag.Name)).IsFalse();
	}

	[Test]
	public async Task GodStillCannotGagAWizard()
	{
		var owner = await Player();
		await Set(1, owner.DbRef, "WIZARD");
		await Assert.That(await Has(owner.DbRef, "WIZARD")).IsTrue();
		await Set(1, owner.DbRef, "GAGGED");
		await Assert.That(await Has(owner.DbRef, "GAGGED")).IsFalse();
	}
}
