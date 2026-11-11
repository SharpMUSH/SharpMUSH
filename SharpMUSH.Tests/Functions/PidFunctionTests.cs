using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using QueueScheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>pidinfo()</c> (<c>src/cque.c:1738-1819</c>) and <c>lpids()</c> (<c>src/cque.c:1825-1923</c>),
/// on a queue of entries a mortal and an object of theirs put there.
/// </summary>
[NotInParallel]
public class PidFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private QueueScheduler Queue => WebAppFactoryArg.Services.GetRequiredService<QueueScheduler>();

	private async Task<string> As(TestIsolationHelpers.TestPlayer who, string expression)
		=> (await WebAppFactoryArg.CommandParserFor(who.DbRef, who.Handle)
			.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	private sealed record Queued(TestIsolationHelpers.TestPlayer Mortal, DBRef Thing, long OwnWait, long ThingWait, long OwnSemaphore);

	private async Task<Queued> QueueThreeAsync(string prefix)
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		var created = await WebAppFactoryArg.CommandParserFor(mortal.DbRef, mortal.Handle)
			.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@create {TestIsolationHelpers.GenerateUniqueName(prefix)}"));
		var thing = DBRef.Parse(created.Message.ToPlainText()!);

		var ownWait = await Queue.AdmitCommandList(MarkupText.Plain($"think {prefix}Own"),
			ParserState.Empty with { Executor = mortal.DbRef }, TimeSpan.FromHours(1));
		var thingWait = await Queue.AdmitCommandList(MarkupText.Plain($"think {prefix}Thing"),
			ParserState.Empty with { Executor = thing }, TimeSpan.FromHours(2));
		var semaphore = await Queue.AdmitCommandList(MarkupText.Plain($"think {prefix}Sem"),
			ParserState.Empty with { Executor = mortal.DbRef }, new DbRefAttribute(mortal.DbRef, ["SEMAPHORE"]), 0);

		return new Queued(mortal, thing, ownWait.Pid!.Value, thingWait.Pid!.Value, semaphore.Pid!.Value);
	}

	private async Task HaltAsync(Queued queued)
	{
		await Queue.HaltByPid(queued.OwnWait);
		await Queue.HaltByPid(queued.ThingWait);
		await Queue.HaltByPid(queued.OwnSemaphore);
	}

	[Test]
	public async ValueTask LpidsGroupsByOwnerWaitQueueFirst()
	{
		var queued = await QueueThreeAsync("LpidsOwner");
		try
		{
			await Assert.That(await As(queued.Mortal, "lpids(me)"))
				.IsEqualTo($"{queued.OwnWait} {queued.ThingWait} {queued.OwnSemaphore}");
			await Assert.That(await As(queued.Mortal, "lpids()"))
				.IsEqualTo($"{queued.OwnWait} {queued.ThingWait} {queued.OwnSemaphore}");
			await Assert.That(await As(queued.Mortal, "lpids(me,independent)"))
				.IsEqualTo($"{queued.OwnWait} {queued.OwnSemaphore}");
			await Assert.That(await As(queued.Mortal, $"lpids({queued.Thing},wait independent)"))
				.IsEqualTo($"{queued.ThingWait}");
			await Assert.That(await As(queued.Mortal, "lpids(me,semaphore)")).IsEqualTo($"{queued.OwnSemaphore}");
			await Assert.That(await As(queued.Mortal, "lpids(me,bogus)")).IsEqualTo("#-1 INVALID ARGUMENT");
		}
		finally { await HaltAsync(queued); }
	}

	[Test]
	public async ValueTask LpidsOfAnotherPlayerIsRefused()
	{
		var queued = await QueueThreeAsync("LpidsOther");
		var other = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "LpidsStranger");
		try
		{
			await Assert.That(await As(other, $"lpids({queued.Mortal.DbRef})")).IsEqualTo("#-1 PERMISSION DENIED: NOT CONTROLLED");
			await Assert.That(await As(other, "lpids(all)")).DoesNotContain($"{queued.OwnWait}");
		}
		finally { await HaltAsync(queued); }
	}

	[Test]
	public async ValueTask PidinfoGivesEveryFieldByDefault()
	{
		var queued = await QueueThreeAsync("PidinfoAll");
		var self = $"#{queued.Mortal.DbRef.Number}";
		try
		{
			await Assert.That(await As(queued.Mortal, $"pidinfo({queued.OwnSemaphore})"))
				.IsEqualTo($"semaphore {self} -1 {self} SEMAPHORE think PidinfoAllSem");

			var wait = (await As(queued.Mortal, $"pidinfo({queued.ThingWait},q ex ti o a c,|)")).Split('|');
			await Assert.That(wait[0]).IsEqualTo("wait");
			await Assert.That(wait[1]).IsEqualTo($"#{queued.Thing.Number}");
			await Assert.That(long.Parse(wait[2])).IsBetween(7000, 7200);
			await Assert.That(wait[3..]).IsEquivalentTo(new[] { "#-1", "#-1", "think PidinfoAllThing" });
		}
		finally { await HaltAsync(queued); }
	}

	[Test]
	public async ValueTask PidinfoSkipsUnknownFieldsAndRefusesAMissingPid()
	{
		var queued = await QueueThreeAsync("PidinfoSkip");
		try
		{
			await Assert.That(await As(queued.Mortal, $"pidinfo({queued.OwnWait},nonsense command)"))
				.IsEqualTo("think PidinfoSkipOwn");
			await Assert.That(await As(queued.Mortal, "pidinfo(999999999)")).IsEqualTo("#-1 INVALID PID");
		}
		finally { await HaltAsync(queued); }
	}

	[Test]
	public async ValueTask PidListsWhatTheCallerMaySee()
	{
		var queued = await QueueThreeAsync("RhostPid");
		var self = $"#{queued.Mortal.DbRef.Number}";
		var thing = $"#{queued.Thing.Number}";
		try
		{
			await Assert.That(await As(queued.Mortal, "pid()"))
				.IsEqualTo($"{queued.OwnWait} {queued.ThingWait} {queued.OwnSemaphore}");
			await Assert.That(await As(queued.Mortal, "pid(7,~)"))
				.IsEqualTo($"{queued.OwnWait}|{self}|{self}~{queued.ThingWait}|{thing}|{thing}~{queued.OwnSemaphore}|{self}|{self}");
			await Assert.That(await As(queued.Mortal, "pid(0,,w)")).IsEqualTo($"{queued.OwnWait} {queued.ThingWait}");
			await Assert.That(await As(queued.Mortal, "pid(0,,S)")).IsEqualTo($"{queued.OwnSemaphore}");
			await Assert.That(await As(queued.Mortal, "pid(0,,c)")).IsEqualTo("3");
			await Assert.That(await As(queued.Mortal, $"pid(0,,q,{thing})")).IsEqualTo($"{queued.ThingWait}");
			await Assert.That(await As(queued.Mortal, "pid(0,,q,me)")).IsEqualTo($"{queued.OwnWait} {queued.OwnSemaphore}");
			await Assert.That(await As(queued.Mortal, "pid(0,,q,me,1)"))
				.IsEqualTo($"{queued.OwnWait} {queued.ThingWait} {queued.OwnSemaphore}");
			await Assert.That(await As(queued.Mortal, $"pid(0,,q,{queued.OwnSemaphore})")).IsEqualTo($"{queued.OwnSemaphore}");
			await Assert.That(await As(queued.Mortal, "pid(48,,s)")).IsEqualTo("RUNNING|think RhostPidSem");
			await Assert.That(await As(queued.Mortal, "pid(8,,s)")).IsEqualTo("0");
			await Assert.That(await As(queued.Mortal, "pid(0,,x)")).IsEqualTo("#-1 INVALID ARGUMENT");
			await Assert.That(await As(queued.Mortal, "pid(64)")).IsEqualTo("#-1");
		}
		finally { await HaltAsync(queued); }
	}

	[Test]
	public async ValueTask PidHidesAnotherPlayersEntries()
	{
		var queued = await QueueThreeAsync("RhostPidOther");
		var other = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RhostPidStranger");
		try
		{
			await Assert.That(await As(other, "pid(0,,c)")).IsEqualTo("0");
			await Assert.That(await As(other, $"pid(0,,q,{queued.OwnWait})")).IsEqualTo(string.Empty);
			await Assert.That(await As(other, $"pid(0,,q,#{queued.Mortal.DbRef.Number})"))
				.IsEqualTo("#-1 PERMISSION DENIED: NOT CONTROLLED, NEEDS GAME.WIZARD OR GAME.ROYALTY OR GAME.SEE_QUEUE");
		}
		finally { await HaltAsync(queued); }
	}

	[Test]
	public async ValueTask PidNamesTheEntryThatIsRunning()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RhostPidRunning");
		const string list = "&PIDSEEN me=[pid(64)]/[pid(128)]";
		var admitted = await Queue.AdmitCommandList(MarkupText.Plain(list),
			ParserState.Empty with { Executor = mortal.DbRef, Enactor = mortal.DbRef }, TimeSpan.FromHours(1));
		var pid = admitted.Pid!.Value;

		await Queue.ReleaseScheduledWork(pid);
		await Queue.DrainImmediateQueueForTests();

		await Assert.That(await As(mortal, "get(me/PIDSEEN)")).IsEqualTo($"{pid}/{list}");
	}

	/// <summary>The example in <c>help pid()</c>.</summary>
	[Test]
	public async ValueTask PidHelpExample()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RhostPidHelp");
		var parser = WebAppFactoryArg.CommandParserFor(mortal.DbRef, mortal.Handle);
		await parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain("@wait 300=think Hello"));
		await parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain("@wait me=think Done"));
		var pids = (await As(mortal, "pid()")).Split(' ').Select(long.Parse).ToArray();
		var self = $"#{mortal.DbRef.Number}";
		try
		{
			await Assert.That(pids).Count().IsEqualTo(2);
			await Assert.That(await As(mortal, "pid(19,~)")).IsEqualTo($"{pids[0]}|{self}|RUNNING~{pids[1]}|{self}|RUNNING");
			await Assert.That(await As(mortal, "pid(32,,s)")).IsEqualTo("think Done");
			await Assert.That(await As(mortal, "pid(0,,c)")).IsEqualTo("2");
		}
		finally
		{
			foreach (var pid in pids) await Queue.HaltByPid(pid);
		}
	}

	/// <summary>
	/// pid(128) in code another player's entry calls answers nothing: the caller may not see that entry, so it may
	/// not read what was queued in it.
	/// </summary>
	[Test]
	public async ValueTask PidHidesTheRunningCommandFromCodeItCalls()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RhostPidPeek");
		var created = await WebAppFactoryArg.CommandParserFor(mortal.DbRef, mortal.Handle)
			.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@create {TestIsolationHelpers.GenerateUniqueName("RhostPidPeek")}"));
		var thing = DBRef.Parse(created.Message.ToPlainText()!);
		await WebAppFactoryArg.CommandParserFor(mortal.DbRef, mortal.Handle)
			.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"&PEEK #{thing.Number}=<[pid(128)]>"));

		var admitted = await Queue.AdmitCommandList(MarkupText.Plain($"&SEEN #{thing.Number}=[u(#{thing.Number}/PEEK)] secret"),
			ParserState.Empty with { Executor = new DBRef(1), Enactor = new DBRef(1) }, TimeSpan.FromHours(1));
		await Queue.ReleaseScheduledWork(admitted.Pid!.Value);
		await Queue.DrainImmediateQueueForTests();

		await Assert.That(await As(mortal, $"get(#{thing.Number}/SEEN)")).IsEqualTo("<> secret");
	}
}
