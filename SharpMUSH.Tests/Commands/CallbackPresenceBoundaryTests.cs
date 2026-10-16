using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class CallbackPresenceBoundaryTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private Task Cmd(string command) => Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(command)).AsTask();
	private async Task<string> Eval(string code) => (await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain(code))).ToPlainText();
	private ParserState Parent(bool nested)
	{
		var state = ParserState.RootFor(Factory.ExecutorDBRef);
		if (!nested) return state;
		var arguments = new Dictionary<string, CallState> { ["parent"] = new("unrelated") };
		return state with { EnvironmentRegisters = arguments, SuppliedArgumentKeys = AttributeArgumentEnvironment.SuppliedKeys(arguments) };
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task MapCallbacksOwnPresenceAcrossInlineAndActualQueuedExecution(bool nested, bool queued)
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "MapPresence");
		await Cmd($"&RUN {thing}=&OUT {thing}=%0|[hasarg(0)]|[hasarg(0,supplied)]|[hasarg(1,supplied)]|[hasarg(parent,supplied)]");
		await Factory.CommandParser.FromState(Parent(nested)).CommandListParse(MarkupText.Plain($"@map{(queued ? "" : "/inline")} {thing}/RUN=x"));
		await Factory.QueueBarrierAsync();
		await Assert.That(await Eval($"get({thing}/OUT)")).IsEqualTo("x|1|1|1|0");
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task PinnedAndUnpinnedMessageFormatsOwnPresence(bool nested, bool pinned)
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "FormatPresence");
		await Cmd($"&FMT {thing}=%0|[hasarg(0)]|[hasarg(0,supplied)]|[hasarg(parent,supplied)]");
		await Factory.FunctionParser.FromState(Parent(nested)).FunctionParse(MarkupText.Plain($"message({thing},fallback,{(pinned ? thing + "/" : "")}FMT,Bob)"));
		await Assert.That(Factory.Notifications.For(thing).Single()).IsEqualTo("Bob|1|1|0");
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task DidItQueuedActionsOwnPresenceIncludingZeroInputInvocations(bool nested, bool empty)
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "DidItPresence");
		await Cmd($"&ACTION {thing}=&OUT {thing}=[hasarg(0)]|[hasarg(0,supplied)]|[hasarg(parent,supplied)]");
		var actor = (await Mediator.Send(new GetObjectNodeQuery(Factory.ExecutorDBRef))).Expect<AnySharpObject>();
		var target = (await Mediator.Send(new GetObjectNodeQuery(thing))).Expect<AnySharpObject>();
		await Factory.Services.GetRequiredService<IDidItService>().DidIt(Factory.CommandParser.FromState(Parent(nested)), new DidItRequest(actor, target, AWhat: "ACTION", Env0: empty ? null : "x"));
		await Factory.QueueBarrierAsync();
		await Assert.That(await Eval($"get({thing}/OUT)")).IsEqualTo(empty ? "0|0|0" : "1|1|0");
	}
}
