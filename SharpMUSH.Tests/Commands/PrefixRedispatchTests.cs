using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>Source-derived prefix cases; these are not captured PennMUSH telnet transcripts.</summary>
[NotInParallel]
public class PrefixRedispatchTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private TestIsolationHelpers.TestPlayer? _actor;
	private string _name = "";
	[Before(Test)]
	public async Task CreateActor()
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		_actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, Connections, "PrefixActor");
		_name = (await mediator.Send(new GetObjectNodeQuery(_actor.DbRef))).Expect<AnySharpObject>().Object().Name;
		var room = await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@dig {Guid.NewGuid():N}"));
		await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@tel {_actor.DbRef}={room.Message}"));
	}
	[After(Test)]
	public async Task DisconnectActor()
	{
		if (_actor is not null) await Connections.Disconnect(_actor.Handle);
	}
	private ValueTask<CallState> Run(string text) => Factory.CommandParserFor(_actor!.DbRef, _actor.Handle)
		.CommandParse(_actor.Handle, Connections, MarkupText.Plain(text));

	[Test]
	[Arguments("]\"[add(1,2)]", "You say, \"[add(1,2)]\"")]
	[Arguments("]say [add(1,2)]", "You say, \"[add(1,2)]\"")]
	[Arguments("]:[add(1,2)]", "{name} [add(1,2)]")]
	[Arguments("]pose [add(1,2)]", "{name} [add(1,2)]")]
	[Arguments("];[add(1,2)]", "{name}[add(1,2)]")]
	[Arguments("]semipose [add(1,2)]", "{name}[add(1,2)]")]
	[Arguments("]\\[add(1,2)]", "[add(1,2)]")]
	[Arguments("]@emit [add(1,2)]", "[add(1,2)]")]
	[Arguments("  ]\"  A   B  ", "You say, \"A   B  \"")]
	[Arguments("]   say A   B  ", "You say, \"A   B  \"")]
	[Arguments("]say {a  b}", "You say, \"{a  b}\"")]
	[Arguments("~\"[add(1,2)]", "You say, \"3\"")]
	[Arguments("~say [add(1,2)]", "You say, \"3\"")]
	[Arguments("~\\A   B", "A   B")]
	[Arguments("~]say [add(1,2)]", "You say, \"[add(1,2)]\"")]
	[Arguments("]~say [add(1,2)]", "You say, \"[add(1,2)]\"")]
	[Arguments("]say %u", "You say, \"%u\"")]
	[Arguments("~@emit %u", "~@emit %u")]
	[Arguments("~~@emit %u", "~~@emit %u")]
	public async Task PrefixPreservesExactlyOneCommand(string text, string expected)
	{
		var before = Factory.Notifications.CountFor(_actor!.DbRef);
		var result = await Run(text);
		await Assert.That(result.HadErrors).IsFalse();
		await Assert.That(Factory.Notifications.For(_actor.DbRef).Skip(before))
			.IsEquivalentTo(new[] { expected.Replace("{name}", _name) });
	}

	[Test]
	[Arguments("]")]
	[Arguments("~")]
	[Arguments("]   ")]
	[Arguments("~   ")]
	[Arguments("]]~ ")]
	public async Task EmptyPrefixTerminatesWithoutDelivery(string text)
	{
		var before = Factory.Notifications.CountFor(_actor!.DbRef);
		var result = await Run(text);
		await Assert.That(result.HadErrors).IsFalse();
		await Assert.That(Factory.Notifications.CountFor(_actor.DbRef)).IsEqualTo(before);
	}

	[Test]
	[Arguments("~@emit add(1,2")]
	[Arguments("~~@emit add(1,2")]
	public async Task StrictPrefixRetainsFailureMetadata(string text)
	{
		var result = await Run(text);
		await Assert.That(result.HadErrors).IsTrue();
		await Assert.That(result.Message!.ToPlainText()).Contains("PARSER FAILURE");
	}

	[Test]
	[Arguments("]\\")]
	[Arguments("~\\")]
	[Arguments("]@emit ")]
	public async Task PrefixKeepsOriginalMarkup(string prefix)
	{
		var styled = (await Factory.FunctionParser.FunctionParse(MarkupText.Plain("[ansi(r,Red)]")))!.Message!;
		var before = Factory.Notifications.RawCountFor(_actor!.DbRef);
		await Factory.CommandParserFor(_actor.DbRef, _actor.Handle).CommandParse(_actor.Handle, Connections,
			MarkupText.Concat(MarkupText.Plain(prefix), styled));
		var delivered = Factory.Notifications.RawFor(_actor.DbRef).Skip(before).Single();
		await Assert.That(delivered is MString text && text.Render(MarkupFormat.Ansi) == styled.Render(MarkupFormat.Ansi)).IsTrue();
	}

	[Test]
	[Arguments("~~")]
	[Arguments("]~")]
	[Arguments("~]")]
	public async Task NestedModifiersRespectRemainingDepth(string prefixes)
	{
		var limit = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Limit.MaxDepth;
		var parser = Factory.CommandParserFor(_actor!.DbRef, _actor.Handle);
		parser = parser.FromState(parser.CurrentState with { CommandModifierDepth = limit - 1 });
		var before = Factory.Notifications.CountForHandle(_actor.Handle);
		var result = await parser.CommandParse(MarkupText.Plain($"{prefixes}@emit depth-body"));
		await Assert.That(result.HadErrors).IsTrue();
		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.Call);
		await Assert.That(Factory.Notifications.For(_actor.DbRef)).DoesNotContain("depth-body");
		await Assert.That(Factory.Notifications.ForHandle(_actor.Handle).Skip(before)).IsEquivalentTo(new[] { ErrorMessages.Returns.Call });
		await Assert.That(parser.CurrentState.CommandModifierDepth).IsEqualTo(limit - 1);
	}

	[Test]
	public async Task QueuedCommandStartsWithFreshModifierDepth()
	{
		var limit = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Limit.MaxDepth;
		var parser = Factory.CommandParserFor(_actor!.DbRef, _actor.Handle);
		var state = parser.CurrentState with { CommandModifierDepth = limit };
		var token = $"fresh_{Guid.NewGuid():N}";
		await Factory.Services.GetRequiredService<IMediator>().Send(new AdmitCommandListRequest(
			MarkupText.Plain($"~@emit {token}"), state, new DbRefAttribute(_actor.DbRef, ["PREFIX_TEST"]), -1));
		await Factory.Services.GetRequiredService<ITaskScheduler>().DrainImmediateQueueForTests();
		await Assert.That(Factory.Notifications.For(_actor.DbRef)).Contains(token);
		await Assert.That(state.SnapshotForQueuedAction().CommandModifierDepth).IsEqualTo(0u);
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(false, true)]
	[Arguments(true, true)]
	public async Task AttributeAssignmentRetainsDirectInputAndPrefixSemantics(bool prefixed, bool queued)
	{
		var parser = Factory.CommandParserFor(_actor!.DbRef, _actor.Handle);
		if (queued) parser = parser.FromState(parser.CurrentState with { Handle = null });
		await parser.CommandParse(MarkupText.Plain($"{(prefixed ? "]" : "")}&VALUE me=[add(1,2)]"));
		var value = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"[get({_actor.DbRef}/VALUE)]"));
		await Assert.That(value!.Message!.ToPlainText()).IsEqualTo(queued && !prefixed ? "3" : "[add(1,2)]");
	}

	[Test]
	public async Task NoEvalPrefixKeepsAttributeNameAndObjectUnevaluated()
	{
		await Run("&FOO me=original");
		await Run("]&FOO [num(me)]=changed-object");
		await Run("]&[cat(F,OO)] me=changed-name");
		var value = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"[get({_actor!.DbRef}/FOO)]"));
		await Assert.That(value!.Message!.ToPlainText()).IsEqualTo("original");
	}

	[Test]
	public async Task StrictRedispatchPreservesCommandHistoryProvenanceAndBudget()
	{
		var parser = Factory.CommandParserFor(_actor!.DbRef, _actor.Handle);
		using var budget = ExecutionBudget.FromMilliseconds(5000);
		var state = parser.CurrentState with { Command = "caller-provenance", CommandHistory = new(), ExecutionBudget = budget };
		var before = Factory.Notifications.CountFor(_actor.DbRef);
		var result = await parser.FromState(state).CommandParse(MarkupText.Plain("~~@emit %u"));
		await Assert.That(result.HadErrors).IsFalse();
		await Assert.That(string.Join('|', Factory.Notifications.For(_actor.DbRef).Skip(before))).IsEqualTo("caller-provenance");
		await Assert.That(state.CommandHistory!.Count).IsEqualTo(1);
		await Assert.That(ReferenceEquals(state.ExecutionBudget, budget)).IsTrue();
		await Assert.That(state.CommandModifierDepth).IsEqualTo(0u);
	}
}
