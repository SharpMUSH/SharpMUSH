using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Substitutions;

/// <summary>
/// <c>%c</c> is the raw text of the command that is running; <c>%u</c> is the evaluated text of the last
/// command whose arguments were parsed in the same queue entry (PennMUSH <c>pe_info-&gt;cmd_raw</c> and
/// <c>cmd_evaled</c>). Every expectation mirrors a PennMUSH 1.8.8 (95ad3511d) transcript taken through
/// tools/oracle. Object names differ, and the /noeval case uses <c>@emit</c> where the transcript used
/// <c>@pemit</c>, because SharpMUSH still evaluates the right side of an <c>=</c> command under /noeval.
/// </summary>
[NotInParallel]
public class CommandTextSubstitutionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private TestIsolationHelpers.TestPlayer _actor = null!;
	private string _room = "";

	[Before(Test)]
	public async Task CreateActor()
	{
		_actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "CmdText");
		_room = (await God($"@dig {Guid.NewGuid():N}")).Message!.ToPlainText();
		await God($"@tel {_actor.DbRef}={_room}");
	}

	[After(Test)]
	public async Task DisconnectActor() => await Connections.Disconnect(_actor.Handle);

	private ValueTask<CallState> God(string text)
		=> Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(text));

	private ValueTask<CallState> Run(string text)
		=> Factory.CommandParserFor(_actor.DbRef, _actor.Handle).CommandParse(_actor.Handle, Connections, MarkupText.Plain(text));

	/// <summary>Runs <paramref name="list"/> as the actor's own queue entry.</summary>
	private async Task<List<string>> Queued(string list)
	{
		var before = Factory.Notifications.CountFor(_actor.DbRef);
		await Mediator.Send(new AdmitCommandListRequest(MarkupText.Plain(list),
			Factory.CommandParserFor(_actor.DbRef, _actor.Handle).CurrentState,
			new DbRefAttribute(_actor.DbRef, ["CMDTEXT"]), -1));
		await Factory.Services.GetRequiredService<ITaskScheduler>().DrainImmediateQueueForTests();
		return Factory.Notifications.For(_actor.DbRef).Skip(before).ToList();
	}

	private async Task<List<string>> Direct(string text)
	{
		var before = Factory.Notifications.CountFor(_actor.DbRef);
		await Run(text);
		return Factory.Notifications.For(_actor.DbRef).Skip(before).ToList();
	}

	[Test]
	public async Task DirectInputSeesTheTypedLineAndNoEvaluatedCommandYet()
	{
		// Penn sets cmd_evaled only once command_parse has rebuilt the line, after the arguments that
		// read it were evaluated; a fresh socket entry has nothing before that.
		await Assert.That(await Direct("think [add(1,2)] %c|%u")).IsEquivalentTo(["3 think [add(1,2)] %c|%u|"]);
	}

	[Test]
	public async Task QueuedCommandSeesItsOwnRawTextAndThePreviousCommandEvaluated()
	{
		await Assert.That(await Queued("@pemit me=pre [add(1,1)];@pemit me=c=%c u=%u"))
			.Contains("c=@pemit me=c=%c u=%u u=@PEMIT me=pre 2");
	}

	[Test]
	public async Task EvaluatedTextKeepsSwitchesAsTypedAndNoEvalArgumentsRaw()
	{
		var output = await Queued("@pemit me=pre;@emit/noeval ne [add(1,1)] %u;@pemit me=after u=%u");
		await Assert.That(output).Contains("ne [add(1,1)] %u");
		await Assert.That(output).Contains("after u=@EMIT/noeval ne [add(1,1)] %u");
	}

	[Test]
	public async Task QueuedEntryStartsWithNoEvaluatedCommand()
	{
		// A queued @switch action gets a new pe_info (PE_INFO_CLONE), not the list that queued it.
		var output = await Queued("@pemit me=pre [add(1,1)];@switch 1=1,@pemit me=sw c=%c u=%u;@pemit me=postsw u=%u");
		await Assert.That(output).Contains("sw c=@pemit me=sw c=%c u=%u u=");
		await Assert.That(output).Contains("postsw u=@SWITCH 1=1,@pemit me=sw c=%c u=%u");
	}

	[Test]
	public async Task FunctionCalledByACommandSeesThatCommand()
	{
		await Run("&UF me=%c|%u");
		await Assert.That(await Queued("@pemit me=pre [add(2,2)];@pemit me=[u(me/UF)]"))
			.Contains("@pemit me=[u(me/UF)]|@PEMIT me=pre 4");
	}

	[Test]
	public async Task VcReadsTheRunningCommand()
	{
		await Assert.That(await Queued("@pemit me=pre;@pemit me=v=[v(c)]"))
			.Contains("v=@pemit me=v=[v(c)]");
	}

	[Test]
	public async Task IncludeSharesTheParentCommandTextBothWays()
	{
		// PE_INFO_SHARE: the included list starts from @INCLUDE's evaluated text, and whatever it
		// evaluated last is what the parent's next command reads.
		await Run("&INC me=@pemit me=inc c=%c u=%u;@pemit me=inc2 [add(3,3)]");
		var output = await Queued("@pemit me=pre [add(1,1)];@include me/INC;@pemit me=post u=%u");
		await Assert.That(output).Contains("inc c=@pemit me=inc c=%c u=%u u=@INCLUDE me/INC");
		await Assert.That(output).Contains("post u=@PEMIT me=inc2 6");
	}

	[Test]
	public async Task IfElseBranchLeavesItsLastCommandForTheParent()
	{
		await Assert.That(await Queued("@ifelse 1={@pemit me=a [add(1,1)]};@pemit me=after c=%c u=%u"))
			.Contains("after c=@pemit me=after c=%c u=%u u=@PEMIT me=a 2");
	}

	[Test]
	public async Task DollarCommandBodyStartsFreshAndLeavesTheCallerAlone()
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "CmdTextObj");
		await God($"@tel {thing}={_room}");
		var verb = $"cmdtext{Guid.NewGuid():N}"[..20];
		await God($"&CMD {thing}=${verb} *:@pemit me=first c=%c u=%u;@pemit me=second u=%u");

		var before = Factory.Notifications.CountFor(thing);
		var output = await Queued($"{verb} [add(1,1)];@pemit me=after u=%u");
		var body = Factory.Notifications.For(thing).Skip(before).ToList();

		await Assert.That(body).Contains("first c=@pemit me=first c=%c u=%u u=");
		await Assert.That(body).Contains("second u=@PEMIT me=first c=@pemit me=first c=%c u=%u u=");
		// game.c stores the evaluated line before atr_comm_match queues the body.
		await Assert.That(output).Contains($"after u={verb} 2");
	}

	[Test]
	public async Task UnmatchedCommandRecordsItsEvaluatedLine()
	{
		var verb = $"nocmd{Guid.NewGuid():N}"[..20];
		await Assert.That(await Queued($"@pemit me=pre;{verb} [add(4,4)];@pemit me=after u=%u"))
			.Contains($"after u={verb} 8");
	}

	[Test]
	public async Task SpeechTokenKeepsTheTypedLineAndRecordsTheCommandItBecame()
	{
		await Assert.That(await Direct("\"hello %c|%u")).Contains("You say, \"hello \"hello %c|%u|\"");
		await Assert.That(await Queued("@pemit me=pre;\"hi [add(1,1)];@pemit me=after u=%u"))
			.Contains("after u=SAY hi 2");
	}

	[Test]
	public async Task NoEvalTokenIsNotPartOfTheRawText()
	{
		await Assert.That(await Direct("]think %c")).IsEquivalentTo(["%c"]);
		await Assert.That(await Queued("@pemit me=pre;]@pemit me=ne [add(1,1)];@pemit me=after u=%u"))
			.Contains("after u=@PEMIT me=ne [add(1,1)]");
	}

	[Test]
	public async Task ExitRecordsGoto()
	{
		var exit = $"cmdexit{Guid.NewGuid():N}"[..20];
		var destination = (await God($"@dig {Guid.NewGuid():N}")).Message!.ToPlainText();
		// @open builds the exit in the executor's own location, so God stands in the actor's room for it.
		await God($"@teleport/silent me={_room}");
		try
		{
			await God($"@open {exit}={destination}");
		}
		finally
		{
			await God("@teleport/silent me=#0");
		}

		await Assert.That(await Queued($"@pemit me=pre;{exit};@pemit me=after u=%u"))
			.Contains($"after u=GOTO {exit}");
	}

	[Test]
	public async Task HookSeesTheRawAndTheEvaluatedCommand()
	{
		var hook = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "CmdTextHook");
		// Hooks are global: only the actor's commands may produce output while this one is set.
		await God($"&AFT {hook}=[if(strmatch(%n,{_actor.Name}),pemit(%#,ac=%c au=%u))]");
		await God($"@hook/after think={hook},AFT");
		try
		{
			await Assert.That(await Direct("think [add(3,4)] %c"))
				.IsEquivalentTo(["7 think [add(3,4)] %c", "ac=think [add(3,4)] %c au=THINK 7 think [add(3,4)] %c"]);
		}
		finally
		{
			await God("@hook/after think");
		}
	}
}
