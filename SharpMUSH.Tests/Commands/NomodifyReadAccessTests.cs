using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using QueueScheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>protection_flags</c>: NOMODIFY takes away the right to change an object, not to read or find it. Its owner
/// still finds it, searches from it, and sees its queue; changing it, or its jobs, is still refused.
/// </summary>
/// <remarks><c>[NotInParallel]</c>: the queue cases park an entry on the session-wide scheduler, as
/// <see cref="QueueInspectionPermissionTests"/> does.</remarks>
[NotInParallel]
public class NomodifyReadAccessTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _owner = null!;

	[Before(Test)]
	public async Task CreateOwner()
	{
		_owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "Keeper");
		if (await Mediator.Send(new GetObjectFlagQuery("NOMODIFY")) is null)
		{
			await Mediator.Send(new CreateObjectFlagCommand("NOMODIFY", null, "", false, ["wizard"], ["wizard"], ["ROOM", "THING", "EXIT", "PLAYER"]));
		}
	}

	[After(Test)]
	public async Task Disconnect() => await ConnectionService.Disconnect(_owner.Handle);

	private Task<string> AsOwner(string command) => CmdAs(_owner.DbRef, _owner.Handle, command);

	private static IDisposable Protecting()
		=> TestOptionsOverride.Scope(options => options with { Compatibility = options.Compatibility with { ProtectionFlags = true } });

	/// <summary>An object the owner made and set NOMODIFY, with a $-command set on it first.</summary>
	private async Task<(string Name, DBRef Id)> ProtectedThingAsync(string command = "")
	{
		var name = TestIsolationHelpers.GenerateUniqueName("Ledger");
		var number = DBRef.Parse(await AsOwner($"@create {name}")).Number;
		if (command.Length > 0)
		{
			await AsOwner($"&CMD #{number}=${command}:think {name}");
			await AsOwner($"@set #{number}=!NO_COMMAND");
		}
		await Cmd($"@set #{number}=NOMODIFY");
		var node = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(number)))).Expect<AnySharpObject>();
		return (name, node.Object().DBRef);
	}

	[Test]
	public async Task LocateSearchesFromARemoteNomodifyLooker()
	{
		var (_, looker) = await ProtectedThingAsync();
		var neighbourName = TestIsolationHelpers.GenerateUniqueName("Crate");
		var neighbour = DBRef.Parse(await Cmd($"@create {neighbourName}")).Number;
		var room = DBRef.Parse(await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName("Vault")}")).Number;
		await Cmd($"@tel #{looker.Number}=#{room}");
		await Cmd($"@tel #{neighbour}=#{room}");

		using (Protecting())
		{
			await Assert.That(await EvalAs(_owner.DbRef, $"locate(#{looker.Number},{neighbourName},N)")).IsEqualTo($"#{neighbour}");
		}
	}

	[Test]
	public async Task FindListsIt()
	{
		var (name, thing) = await ProtectedThingAsync();

		using (Protecting())
		{
			await Assert.That(await AsOwner($"@find {name}")).IsEqualTo($"#{thing.Number}");
		}
	}

	[Test]
	public async Task ScanReportsItsCommands()
	{
		var word = TestIsolationHelpers.GenerateUniqueName("xyzzy").ToLowerInvariant();
		var (_, thing) = await ProtectedThingAsync($"{word} *");

		using (Protecting())
		{
			await Assert.That(await AsOwner($"@scan/self {word} now")).Contains($"#{thing.Number}/CMD");
		}
	}

	[Test]
	public async Task ItsQueueCanBeReadButNotChanged()
	{
		var (name, thing) = await ProtectedThingAsync();
		var queue = WebAppFactoryArg.Services.GetRequiredService<QueueScheduler>();
		var job = await queue.AdmitCommandList(MarkupText.Plain($"think {name}"),
			ParserState.Empty with { Executor = thing }, new DbRefAttribute(thing, ["SEMAPHORE"]), 1);
		try
		{
			using (Protecting())
			{
				await Assert.That(await AsOwner($"@ps #{thing.Number}")).DoesNotStartWith("#-1");
				await Assert.That(await AsOwner($"@ps/debug {job.Pid}")).DoesNotStartWith("#-1");
				await Assert.That(await EvalAs(_owner.DbRef, $"pidinfo({job.Pid},command)")).Contains(name);
				await Assert.That(await AsOwner($"@halt/pid {job.Pid}")).StartsWith("#-1 PERMISSION DENIED");
			}
		}
		finally { if (job.Pid is { } pid) await queue.HaltByPid(pid); }
	}

	[Test]
	public async Task ThePortalShowsItsJobsButDoesNotPauseThem()
	{
		var (_, thing) = await ProtectedThingAsync();
		var actor = new CapabilityActor("keeper", _owner.DbRef, _owner.DbRef);
		var entry = new QueueEntrySnapshot(77, thing, _owner.DbRef, "delay", QueueEntryState.Pending, TimeSpan.FromSeconds(30), "");
		var queue = Substitute.For<ITaskQueueReader>();
		queue.GetQueueEntry(77).Returns(entry);
		queue.GetQueueEntries().Returns([entry]);
		var control = Substitute.For<ITaskQueueControl>();
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.GetGameActorAsync(_owner.DbRef, Arg.Any<CancellationToken>()).Returns(actor);
		capabilities.GetGrantedScopesAsync(actor, Arg.Any<CancellationToken>())
			.Returns(new HashSet<string> { PortalPermission.QueueInspectOwn, PortalPermission.QueueControlOwn });
		var service = new QueueControlService(queue, control, capabilities, Mediator,
			WebAppFactoryArg.Services.GetRequiredService<IPermissionService>());

		using (Protecting())
		{
			await Assert.That((await service.ListAsync(actor)).Single()).IsEqualTo(entry);
			await Assert.That(await service.ChangeAsync(actor, 77, false)).IsEqualTo(QueueControlResult.NotFound);
		}

		await control.DidNotReceiveWithAnyArgs().PausePending(default, default!);
	}
}
