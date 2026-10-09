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
		// Created in a room of its own rather than teleported there: the look after a move is queued, and
		// could otherwise arrive inside the window a test counts.
		var room = await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@dig {Guid.NewGuid():N}"));
		_actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, Connections, "PrefixActor",
			DBRef.Parse(room.Message.ToPlainText().Trim()));
		_name = (await mediator.Send(new GetObjectNodeQuery(_actor.DbRef))).Expect<AnySharpObject>().Object().Name;
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
	// %c is the command the modifier re-dispatched, without the modifier (PennMUSH strips `]` from cmd_raw).
	[Arguments("~@emit %c", "@emit %c")]
	[Arguments("~~@emit %c", "@emit %c")]
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
		await Assert.That(result.Message.ToPlainText()).Contains("PARSER FAILURE");
	}

	[Test]
	[Arguments("]\\")]
	[Arguments("~\\")]
	[Arguments("]@emit ")]
	public async Task PrefixKeepsOriginalMarkup(string prefix)
	{
		// Unique text, and selected by it rather than by being the only thing in the window (#1247):
		// .Single() over everything that arrived throws rather than failing an assertion.
		var marker = $"Red{Guid.NewGuid():N}"[..11];
		var styled = await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain($"[ansi(r,{marker})]"));
		var before = Factory.Notifications.RawCountFor(_actor!.DbRef);
		await Factory.CommandParserFor(_actor.DbRef, _actor.Handle).CommandParse(_actor.Handle, Connections,
			MarkupText.Concat(MarkupText.Plain(prefix), styled));
		var delivered = Factory.Notifications.RawFor(_actor.DbRef).Skip(before)
			.SingleOrDefault(message => message is MString markup && markup.ToPlainText() == marker);
		await Assert.That(delivered is MString text && text.Render(MarkupFormat.Ansi) == styled.Render(MarkupFormat.Ansi))
			.IsTrue().Because($"the redispatched line must reach the actor as markup carrying {marker}");
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
		await Assert.That(result.Message.ToPlainText()).IsEqualTo(ErrorMessages.Returns.Call);
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
		await Factory.Notifications.WaitForAsync(_actor.DbRef, token);
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
		var value = await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain($"[get({_actor.DbRef}/VALUE)]"));
		await Assert.That(value.ToPlainText()).IsEqualTo(queued && !prefixed ? "3" : "[add(1,2)]");
	}

	[Test]
	public async Task NoEvalPrefixKeepsAttributeNameAndObjectUnevaluated()
	{
		await Run("&FOO me=original");
		await Run("]&FOO [num(me)]=changed-object");
		await Run("]&[cat(F,OO)] me=changed-name");
		var value = await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain($"[get({_actor!.DbRef}/FOO)]"));
		await Assert.That(value.ToPlainText()).IsEqualTo("original");
	}

	[Test]
	public async Task StrictRedispatchPreservesCommandHistoryProvenanceAndBudget()
	{
		var parser = Factory.CommandParserFor(_actor!.DbRef, _actor.Handle);
		using var budget = ExecutionBudget.FromMilliseconds(5000);
		var registers = new Dictionary<string, MString> { ["PROVENANCE"] = MarkupText.Plain("caller-provenance") };
		var state = parser.CurrentState with { Registers = new([registers]), CommandHistory = new(), ExecutionBudget = budget };
		var before = Factory.Notifications.CountFor(_actor.DbRef);
		var result = await parser.FromState(state).CommandParse(MarkupText.Plain("~~@emit %q<PROVENANCE>"));
		await Assert.That(result.HadErrors).IsFalse();
		await Assert.That(string.Join('|', Factory.Notifications.For(_actor.DbRef).Skip(before))).IsEqualTo("caller-provenance");
		await Assert.That(state.CommandHistory!.Count).IsEqualTo(1);
		await Assert.That(ReferenceEquals(state.ExecutionBudget, budget)).IsTrue();
		await Assert.That(state.CommandModifierDepth).IsEqualTo(0u);
	}
}
