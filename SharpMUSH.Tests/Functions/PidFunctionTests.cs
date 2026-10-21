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
			await Assert.That(await As(other, $"lpids({queued.Mortal.DbRef})")).IsEqualTo("#-1 PERMISSION DENIED");
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
}
