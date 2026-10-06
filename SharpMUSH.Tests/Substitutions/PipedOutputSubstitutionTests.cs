using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Substitutions;

/// <summary>
/// <c>%></c> is the logical output of the last command run in the queue entry: what the command's
/// function analog would return (<c>@dig</c> gives what <c>dig()</c> gives), never the text it shows.
/// A command with no output clears it; control flow that runs a list in place leaves that list's last
/// output; a queued entry starts with a copy of the value its submitter had when it queued it.
/// </summary>
public class PipedOutputSubstitutionTests
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
		_room = (await God($"@dig {Guid.NewGuid():N}")).Message!.ToPlainText();
		_actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "PipeOut",
			DBRef.Parse(_room.Trim()));
	}

	[After(Test)]
	public async Task DisconnectActor() => await Connections.Disconnect(_actor.Handle);

	private ValueTask<CallState> God(string text)
		=> Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(text));

	private ValueTask<CallState> Run(string text)
		=> Factory.CommandParserFor(_actor.DbRef, _actor.Handle).CommandParse(_actor.Handle, Connections, MarkupText.Plain(text));

	/// <summary>Runs <paramref name="list"/> as the actor's own queue entry, and what the actor heard.</summary>
	private async Task<List<string>> Queued(string list)
	{
		var before = Factory.Notifications.CountFor(_actor.DbRef);
		await Mediator.Send(new AdmitCommandListRequest(MarkupText.Plain(list),
			Factory.CommandParserFor(_actor.DbRef, _actor.Handle).CurrentState,
			new DbRefAttribute(_actor.DbRef, ["PIPEOUT"]), -1));
		await Factory.Services.GetRequiredService<ITaskScheduler>().SettleForTestsAsync();
		return Factory.Notifications.For(_actor.DbRef).Skip(before).ToList();
	}

	private async Task<List<string>> Direct(string text)
	{
		var before = Factory.Notifications.CountFor(_actor.DbRef);
		await Run(text);
		return Factory.Notifications.For(_actor.DbRef).Skip(before).ToList();
	}

	private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

	[Test]
	public async Task DigOutputsTheRoomAsDigDoes()
	{
		var name = Unique("piperoom");
		var output = await Queued($"@dig {name};@pemit me=dug [name(%>)] [type(%>)]");
		await Assert.That(output).Contains($"dug {name} ROOM");
	}

	[Test]
	public async Task ThinkOutputsWhatItThinks()
	{
		await Assert.That(await Queued("think [add(20,22)];@pemit me=out=%>")).Contains("out=42");
	}

	[Test]
	public async Task CommandWithNoOutputClearsIt()
	{
		var output = await Queued("think pipedvalue;@pemit me=first=%>;@pemit me=second=[strlen(%>)]");
		await Assert.That(output).Contains("first=pipedvalue");
		await Assert.That(output).Contains("second=0");
	}

	[Test]
	public async Task FailedCommandLeavesItsError()
	{
		var missing = Unique("nosuchthing");
		await Assert.That(await Queued($"think before;@destroy {missing};@pemit me=err=[left(%>,3)]"))
			.Contains("err=#-1");
	}

	[Test]
	public async Task PassthroughCommandsLeaveItAlone()
	{
		await Assert.That(await Queued("think kept;@assert 1;@@ comment;@pemit me=after=%>"))
			.Contains("after=kept");
	}

	[Test]
	public async Task BreakActionListLeavesItAlone()
	{
		// @break runs its action list in place; @include/nobreak lets the outer list carry on and read %>.
		await Run("&BRK me=@break 1=think inner");
		await Assert.That(await Queued("think kept;@include/nobreak me/BRK;@pemit me=after=%>")).Contains("after=kept");
	}

	[Test]
	public async Task RetryLeavesTheLastRerunsOutput()
	{
		// The rerun think gets the retry argument as its text; a true condition reruns it until @retry's limit.
		await Assert.That(await Queued("think start;@retry 1=again;@pemit me=after=%>")).Contains("after=again");
	}

	[Test]
	public async Task InPlaceListLeavesItsLastOutput()
	{
		await Run("&INC me=think included");
		await Assert.That(await Queued("think before;@include me/INC;@pemit me=after=%>")).Contains("after=included");
		await Assert.That(await Queued("think before;@switch/inline 1=1,{think switched};@pemit me=after=%>"))
			.Contains("after=switched");
	}

	[Test]
	public async Task QueuedListClearsIt()
	{
		await Assert.That(await Queued("think before;@switch 1=1,{think switched};@pemit me=after=[strlen(%>)]"))
			.Contains("after=0");
	}

	[Test]
	public async Task QueuedEntryStartsWithACopyFromWhenItWasQueued()
	{
		// @wait's command is evaluated when it fires, after this list has moved on to "later".
		var marker = TestIsolationHelpers.GenerateUniqueName("Waited");
		var before = Factory.Notifications.CountFor(_actor.DbRef);
		await Queued($"think atqueue;@wait 0=@pemit me={marker}=%>;think later");
		await Factory.Notifications.WaitForAsync(_actor.DbRef, marker, startIndex: before);
		await Assert.That(Factory.Notifications.For(_actor.DbRef).Skip(before)).Contains($"{marker}=atqueue");
	}

	[Test]
	public async Task FunctionReadsTheRunningListsOutput()
	{
		await Run("&UF me=from u: %>");
		await Assert.That(await Queued("think infunction;@pemit me=[u(me/UF)]")).Contains("from u: infunction");
	}

	[Test]
	public async Task TypedLineStartsEmpty()
	{
		await Run("think typed");
		await Assert.That(await Direct("think x%>y")).IsEquivalentTo(["xy"]);
	}

	[Test]
	public async Task ExitOutputsTheDestination()
	{
		var exit = Unique("pipeexit");
		var destination = (await God($"@dig {Guid.NewGuid():N}")).Message!.ToPlainText();
		await God($"@open {exit}={destination},,{_room}");

		await Assert.That(await Queued($"{exit};@pemit me=at=[num(%>)] [num(here)]"))
			.Contains($"at=#{DBRef.Parse(destination.Trim()).Number} #{DBRef.Parse(destination.Trim()).Number}");
	}

	[Test]
	// The hook is on THINK for the whole game while it is set.
	[NotInParallel]
	public async Task AfterHookReadsTheHookedCommandsOutput()
	{
		var hook = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "PipeHook");
		await God($"&AFT {hook}=[if(strmatch(%n,{_actor.Name}),pemit(%#,hooked=%>))]");
		await God($"@hook/after think={hook},AFT");
		try
		{
			await Assert.That(await Direct("think [add(3,4)]")).IsEquivalentTo(["7", "hooked=7"]);
		}
		finally
		{
			await God("@hook/after think");
		}
	}
}
