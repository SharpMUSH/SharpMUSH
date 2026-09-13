using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using System.Text.Json;

namespace SharpMUSH.Tests.Commands;

public class DefinitionAuthorizationTests
{
	// Keep unrelated parallel letter-allocation tests free to claim their ASCII symbols.
	private const string DefinitionSymbol = "\uE001";
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private readonly List<long> _handles = [];
	[After(Test)]
	public async Task Cleanup()
	{
		foreach (var handle in _handles) await Connections.Disconnect(handle);
	}
	private async Task<TestIsolationHelpers.TestPlayer> Actor(bool wizard)
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "DefinitionActor");
		_handles.Add(actor.Handle);
		if (wizard) await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@set {actor.DbRef}=WIZARD"));
		return actor;
	}
	private async Task<string> Snapshot(string kind, string name) => kind == "flag"
		? JsonSerializer.Serialize(await Mediator.Send(new GetObjectFlagQuery(name)))
		: JsonSerializer.Serialize(await Mediator.Send(new GetPowerQuery(name)));
	private async Task Create(string kind, string name)
	{
		if (kind == "flag") await Mediator.Send(new CreateObjectFlagCommand(name, [name + "_ORIGINAL"], DefinitionSymbol, false, ["FLAG^WIZARD"], ["FLAG^WIZARD"], ["PLAYER"]));
		else await Mediator.Send(new CreatePowerCommand(name, name + "_ORIGINAL", DefinitionSymbol, false, ["FLAG^WIZARD"], ["FLAG^WIZARD"], ["PLAYER"]));
	}
	private async Task Delete(string kind, string name)
	{
		if (kind == "flag") await Mediator.Send(new DeleteObjectFlagCommand(name));
		else await Mediator.Send(new DeletePowerCommand(name));
	}
	[Test]
	[Arguments("flag")]
	[Arguments("power")]
	public async Task RefusedMediatorCreateKeepsWarmLookupAndListCoherent(string kind)
	{
		var name = "AUTH" + Guid.NewGuid().ToString("N").ToUpperInvariant();
		try
		{
			await Assert.That(await Snapshot(kind, name)).IsEqualTo("null");
			if (kind == "flag") _ = await Mediator.CreateStream(new GetAllObjectFlagsQuery()).ToArrayAsync();
			else _ = await Mediator.CreateStream(new GetPowersQuery()).ToArrayAsync();
			await Create(kind, name);
			var before = await Snapshot(kind, name);
			if (kind == "flag")
			{
				await Assert.That(await Mediator.Send(new CreateObjectFlagCommand(name, ["REPLACED"], "X", true, [], [], []))).IsNull();
				var listed = await Mediator.CreateStream(new GetAllObjectFlagsQuery()).Where(flag => flag.Name == name).ToArrayAsync();
				await Assert.That(listed).HasSingleItem();
				await Assert.That(JsonSerializer.Serialize(listed[0])).IsEqualTo(before);
			}
			else
			{
				await Assert.That(await Mediator.Send(new CreatePowerCommand(name, "REPLACED", "X", true, [], [], []))).IsNull();
				var listed = await Mediator.CreateStream(new GetPowersQuery()).Where(power => power.Name == name).ToArrayAsync();
				await Assert.That(listed).HasSingleItem();
				await Assert.That(JsonSerializer.Serialize(listed[0])).IsEqualTo(before);
			}
			await Assert.That(await Snapshot(kind, name)).IsEqualTo(before);
		}
		finally { await Delete(kind, name); }
	}
	public static IEnumerable<(string, string, bool, bool)> MutationCases()
	{
		foreach (var kind in new[] { "flag", "power" })
			foreach (var operation in new[] { "delete", "letter", "type", "alias", "restrict", "disable", "enable" })
				foreach (var wizard in new[] { false, true })
					foreach (var reverse in new[] { false, true })
						yield return (kind, operation, wizard, reverse);
	}
	[Test]
	[Arguments("flag", "alias/letter")]
	[Arguments("flag", "letter/alias")]
	[Arguments("power", "alias/letter")]
	[Arguments("power", "letter/alias")]
	public async Task AggregateSpecificAliasAndLetterPrecedenceIsPreserved(string kind, string switches)
	{
		var name = "AUTH" + Guid.NewGuid().ToString("N").ToUpperInvariant();
		try
		{
			await Create(kind, name);
			await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@{kind}/{switches} {name}={(kind == "flag" ? "" : "R")}"));
			if (kind == "flag")
			{
				var flag = await Mediator.Send(new GetObjectFlagQuery(name));
				await Assert.That(flag!.Symbol).IsEqualTo("");
				await Assert.That(flag.Aliases!).IsEquivalentTo([name + "_ORIGINAL"]);
			}
			else
			{
				var power = await Mediator.Send(new GetPowerQuery(name));
				await Assert.That(power!.Alias).IsEqualTo("R");
				await Assert.That(power.Symbol).IsEqualTo(DefinitionSymbol);
			}
		}
		finally { await Delete(kind, name); }
	}

	[Test]
	[Arguments("flag", "disable/enable")]
	[Arguments("flag", "enable/disable")]
	[Arguments("power", "disable/enable")]
	[Arguments("power", "enable/disable")]
	public async Task DisableWinsItsPairedBranch(string kind, string switches)
	{
		var name = "AUTH" + Guid.NewGuid().ToString("N").ToUpperInvariant();
		try
		{
			await Create(kind, name);
			await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@{kind}/{switches} {name}"));
			var disabled = kind == "flag" ? (await Mediator.Send(new GetObjectFlagQuery(name)))!.Disabled
				: (await Mediator.Send(new GetPowerQuery(name)))!.Disabled;
			await Assert.That(disabled).IsTrue();
		}
		finally { await Delete(kind, name); }
	}
	[Test]
	[MethodDataSource(nameof(MutationCases))]
	public async Task SelectedMutationRequiresGodAndGodStillExecutesIt(string kind, string operation, bool wizard, bool reverse)
	{
		var actor = await Actor(wizard);
		var name = "AUTH" + Guid.NewGuid().ToString("N").ToUpperInvariant();
		try
		{
			await Create(kind, name);
			if (operation == "enable")
			{
				if (kind == "flag") await Mediator.Send(new SetObjectFlagDisabledCommand(name, true));
				else await Mediator.Send(new SetPowerDisabledCommand(name, true));
			}
			var before = await Snapshot(kind, name);
			var extra = kind == "flag" && wizard ? "debug" : "decompile";
			var switches = operation is "disable" or "enable" && !(kind == "flag" && wizard) ? operation
				: reverse ? $"{extra}/{operation}" : $"{operation}/{extra}";
			var argument = operation switch { "type" => "THING", "alias" => name + "_CHANGED", "restrict" => "FLAG^ROYALTY", _ => "" };
			var command = MarkupText.Plain($"@{kind}/{switches} {name}={argument}");
			await Factory.CommandParser.CommandParse(actor.Handle, Connections, command);
			await Assert.That(await Snapshot(kind, name)).IsEqualTo(before);
			await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(Factory.Services.GetRequiredService<INotifyService>(),
				nameof(ErrorMessages.Notifications.NotEnoughMagic), actor.DbRef, actor.DbRef)).IsTrue();
			await Factory.CommandParser.CommandParse(1, Connections, command);
			await Assert.That(await Snapshot(kind, name)).IsNotEqualTo(before);
		}
		finally { await Delete(kind, name); }
	}

	[Test]
	[Arguments("flag", "list")]
	[Arguments("flag", "list/add")]
	[Arguments("flag", "add/list")]
	[Arguments("flag", "decompile")]
	[Arguments("flag", "decompile/disable")]
	[Arguments("flag", "disable/decompile")]
	[Arguments("power", "list")]
	[Arguments("power", "list/add")]
	[Arguments("power", "add/list")]
	[Arguments("power", "decompile")]
	[Arguments("power", "decompile/disable")]
	[Arguments("power", "disable/decompile")]
	public async Task SelectedReadsStayOpenAndNeverMutate(string kind, string switches)
	{
		var actor = await Actor(false);
		var name = "AUTH" + Guid.NewGuid().ToString("N").ToUpperInvariant();
		try
		{
			await Create(kind, name);
			var before = await Snapshot(kind, name);
			var offset = Factory.Notifications.For(actor.DbRef).Count;
			await Factory.CommandParser.CommandParse(actor.Handle, Connections, MarkupText.Plain($"@{kind}/{switches} {name}=REPLACEMENT"));
			await Assert.That(await Snapshot(kind, name)).IsEqualTo(before);
			await Assert.That(Factory.Notifications.For(actor.DbRef).Skip(offset).Any(message => message.Contains(name))).IsTrue();
		}
		finally { await Delete(kind, name); }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task SelectedDebugRequiresWizard(bool wizard)
	{
		var actor = await Actor(wizard);
		var offset = Factory.Notifications.For(actor.DbRef).Count;
		await Factory.CommandParser.CommandParse(actor.Handle, Connections, MarkupText.Plain("@flag/debug WIZARD"));
		await Assert.That(Factory.Notifications.For(actor.DbRef).Skip(offset).Any(message => message.StartsWith("DEBUG - Flag:"))).IsEqualTo(wizard);
		if (!wizard) await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(Factory.Services.GetRequiredService<INotifyService>(),
			nameof(ErrorMessages.Notifications.PermissionDenied), actor.DbRef, actor.DbRef)).IsTrue();
	}

	[Test]
	[Arguments("flag", "add/decompile", false)]
	[Arguments("flag", "decompile/add", false)]
	[Arguments("power", "add/decompile", false)]
	[Arguments("power", "decompile/add", false)]
	[Arguments("flag", "add/debug", true)]
	[Arguments("flag", "debug/add", true)]
	public async Task NonGodCannotCreateThroughMixedReadSwitch(string kind, string switches, bool wizard)
	{
		var actor = await Actor(wizard);
		var name = "AUTH" + Guid.NewGuid().ToString("N").ToUpperInvariant();
		try
		{
			await Factory.CommandParser.CommandParse(actor.Handle, Connections, MarkupText.Plain($"@{kind}/{switches} {name}=Z"));
			if (kind == "flag") await Assert.That(await Mediator.Send(new GetObjectFlagQuery(name))).IsNull();
			else await Assert.That(await Mediator.Send(new GetPowerQuery(name))).IsNull();
			await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@{kind}/{switches} {name}=Z"));
			if (kind == "flag") await Assert.That(await Mediator.Send(new GetObjectFlagQuery(name))).IsNotNull();
			else await Assert.That(await Mediator.Send(new GetPowerQuery(name))).IsNotNull();
		}
		finally
		{
			if (kind == "flag") await Mediator.Send(new DeleteObjectFlagCommand(name));
			else await Mediator.Send(new DeletePowerCommand(name));
		}
	}

	[Test]
	public async Task GodPowerAddCannotReplaceExistingDefinition()
	{
		var name = "AUTH" + Guid.NewGuid().ToString("N").ToUpperInvariant();
		try
		{
			await Mediator.Send(new CreatePowerCommand(name, "ORIGINAL", DefinitionSymbol, false, ["FLAG^ROYALTY"], ["FLAG^WIZARD"], ["THING"]));
			await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@power/add {name}=REPLACEMENT"));
			var stored = await Mediator.Send(new GetPowerQuery(name));
			await Assert.That(stored!.Alias).IsEqualTo("ORIGINAL");
			await Assert.That(stored.Symbol).IsEqualTo(DefinitionSymbol);
			await Assert.That(stored.TypeRestrictions).IsEquivalentTo(["THING"]);
			await Assert.That(Factory.Notifications.For(new DBRef(1)).Where(message => message.Contains(name)))
				.DoesNotContain(message => message.Contains("created", StringComparison.OrdinalIgnoreCase));
		}
		finally { await Mediator.Send(new DeletePowerCommand(name)); }
	}
}
