using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Models.InputSessions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// TinyMUX's and RhostMUSH's <c>@program</c> and <c>@quitprogram</c>, and RhostMUSH's <c>@progreset</c>, on guided
/// input.
/// </summary>
public class DialectProgramTests : ServerTestBase
{
	private IInputSessionService Sessions => WebAppFactoryArg.Services.GetRequiredService<IInputSessionService>();
	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();

	private static IDisposable Dialect(string dialect) => TestOptionsOverride.Scope(options => options with
	{
		Compatibility = options.Compatibility with { Dialect = dialect }
	});

	private async Task<TestIsolationHelpers.TestPlayer> IsolatedPlayerAsync(string prefix)
	{
		var room = DBRef.Parse(await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName($"{prefix}Room")}"));
		return await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, room);
	}

	/// <summary>A thing the player owns whose DO attribute stores the line it is given on the player.</summary>
	private async Task<DBRef> ProgramHolderAsync(TestIsolationHelpers.TestPlayer actor)
	{
		var thing = DBRef.Parse(await CmdAs(actor.DbRef, actor.Handle, $"@create {TestIsolationHelpers.GenerateUniqueName("Prog")}"));
		await CmdAs(actor.DbRef, actor.Handle, $"&DO {thing}=&GOT %#=%0;&REG %#=%q0");
		return thing;
	}

	private async Task TypeAsync(TestIsolationHelpers.TestPlayer actor, string line)
	{
		await Scheduler.AdmitUserCommand(actor.Handle, MarkupText.Plain(line), ParserState.Empty with { Handle = actor.Handle });
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private bool InProgram(TestIsolationHelpers.TestPlayer actor)
		=> Sessions.GetCapturing(actor.Handle) is { Match: InputMatch.Program };

	private bool Heard(TestIsolationHelpers.TestPlayer actor, string text)
		=> Notifications.For(actor.DbRef).Any(line => line.Contains(text, StringComparison.Ordinal));

	[Test]
	[Arguments("mux")]
	[Arguments("rhost")]
	public async Task ProgramRunsTheAttributeOnTheNextLine(string dialect)
	{
		var actor = await IsolatedPlayerAsync("Program");
		var holder = await ProgramHolderAsync(actor);
		var shown = TestIsolationHelpers.GenerateUniqueName("ask");
		var typed = TestIsolationHelpers.GenerateUniqueName("line");
		try
		{
			using (Dialect(dialect))
				await CmdAs(actor.DbRef, actor.Handle, $"@program me=[setq(0,{shown}reg)]{holder}/DO:{shown}");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Heard(actor, shown)).IsTrue();
			await Assert.That(InProgram(actor)).IsTrue();

			await TypeAsync(actor, typed);

			await Assert.That(await EvalAs(actor.DbRef, "get(me/GOT)")).IsEqualTo(typed);
			await Assert.That(await EvalAs(actor.DbRef, "get(me/REG)")).IsEqualTo($"{shown}reg");
			await Assert.That(InProgram(actor)).IsFalse();
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task PipedLineRunsAsACommandAndQuitProgramEndsIt()
	{
		var actor = await IsolatedPlayerAsync("ProgramPipe");
		var holder = await ProgramHolderAsync(actor);
		var piped = TestIsolationHelpers.GenerateUniqueName("piped");
		try
		{
			using (Dialect("mux"))
			{
				await CmdAs(actor.DbRef, actor.Handle, $"@program me={holder}/DO");
				await WebAppFactoryArg.QueueBarrierAsync();
				await TypeAsync(actor, $"|&PIPED me={piped}");

				await Assert.That(await EvalAs(actor.DbRef, "get(me/PIPED)")).IsEqualTo(piped);
				await Assert.That(InProgram(actor)).IsTrue();

				await CmdAs(actor.DbRef, actor.Handle, "@quitprogram");
				await WebAppFactoryArg.QueueBarrierAsync();
			}

			await Assert.That(InProgram(actor)).IsFalse();
			await Assert.That(Heard(actor, "You have aborted your program.")).IsTrue();
			await Assert.That(await EvalAs(actor.DbRef, "hasattr(me,GOT)")).IsEqualTo("0");
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task ProgramRefusesASecondProgramAndAnotherPlayersCharacter()
	{
		var actor = await IsolatedPlayerAsync("ProgramTwice");
		var other = await IsolatedPlayerAsync("ProgramOther");
		var holder = await ProgramHolderAsync(actor);
		try
		{
			using (Dialect("mux"))
			{
				await CmdAs(actor.DbRef, actor.Handle, $"@program me={holder}/DO");
				await CmdAs(actor.DbRef, actor.Handle, $"@program me={holder}/DO");
				await CmdAs(actor.DbRef, actor.Handle, $"@program {other.DbRef}={holder}/DO");
				await WebAppFactoryArg.QueueBarrierAsync();
			}

			await Assert.That(Heard(actor, "You are already in a program.")).IsTrue();
			await Assert.That(Sessions.GetCapturing(other.Handle)).IsNull();
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
			await ConnectionService.Disconnect(other.Handle);
		}
	}

	[Test]
	public async Task ProgResetSetsAPromptOnlyInAProgram()
	{
		var actor = await IsolatedPlayerAsync("ProgReset");
		var holder = await ProgramHolderAsync(actor);
		try
		{
			using (Dialect("rhost"))
			{
				await CmdAs(actor.DbRef, actor.Handle, "@progreset me=Name?");
				await WebAppFactoryArg.QueueBarrierAsync();
				await Assert.That(Heard(actor, "Can not assign a prompt to someone not in a program.")).IsTrue();

				await CmdAs(actor.DbRef, actor.Handle, $"@program me={holder}/DO");
				await CmdAs(actor.DbRef, actor.Handle, "@progreset me=Name?");
				await WebAppFactoryArg.QueueBarrierAsync();
			}

			await Assert.That(Heard(actor, "Program prompt customized.")).IsTrue();
			await Assert.That(InProgram(actor)).IsTrue();
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}
}
