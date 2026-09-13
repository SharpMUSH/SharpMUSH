using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public partial class SpeechTransformationTests
{
	private sealed record FixedOptions(SharpMUSHOptions CurrentValue) : IOptionsWrapper<SharpMUSHOptions>;
	private SharpMUSHOptions Options => Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
	private IAttributeService Attributes => Factory.Services.GetRequiredService<IAttributeService>();
	private MUSHCodeParser ConfiguredParser(TestIsolationHelpers.TestPlayer actor, SharpMUSHOptions options)
	{
		var configuration = new FixedOptions(options);
		var speech = new Lazy<SpeechService>(() => new SpeechService(Attributes, configuration));
		var communication = ActivatorUtilities.CreateInstance<CommunicationService>(Factory.Services, speech);
		var functions = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Functions.Functions>(Factory.Services, configuration, communication);
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(Factory.Services, configuration, communication, functions);
		return (MUSHCodeParser)Factory.CommandParserFor(actor.DbRef, actor.Handle) with
		{
			Configuration = configuration,
			CommandLibrary = commands.Get(),
			FunctionLibrary = functions.Get()
		};
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task QuoteStrippingUsesConfiguredValue(bool strip)
	{
		var actor = await Player();
		await Room(actor);
		var parser = ConfiguredParser(actor, Options with { Cosmetic = Options.Cosmetic with { ChatStripQuote = strip } });
		await parser.CommandParse(actor.Handle, Connections, MarkupText.Plain("say \"hello"));
		await Assert.That(Factory.Notifications.For(actor.DbRef).Contains(strip ? "You say, \"hello\"" : "You say, \"\"hello\"")).IsTrue();
	}

	[Test]
	[Arguments("", "original")]
	[Arguments("[div(1,0)]", "#-1 DIVISION BY ZERO")]
	[Arguments("before[div(1,0)]after", "before#-1 DIVISION BY ZEROafter")]
	public async Task EmptyExpansionFallsBackButFunctionErrorTextIsSpeech(string code, string expected)
	{
		var actor = await Player();
		await Room(actor);
		await Admin($"&SPEECHMOD {actor.DbRef}={code}");
		await Command(actor, "@emit original");
		await Assert.That(Factory.Notifications.For(actor.DbRef).Contains(expected)).IsTrue();
	}

	[Test]
	public async Task TransformUsesExecutorIdentityAndLocalizesQRegisters()
	{
		var actor = await Player();
		var enactor = await Player();
		await Admin($"&SPEECHMOD {actor.DbRef}=[setq(A,changed)]%#|%n|%qA|%0");
		var parser = Factory.CommandParserFor(actor.DbRef, actor.Handle);
		await parser.With(state => state with
		{
			Enactor = enactor.DbRef,
			Registers = new([new Dictionary<string, MarkupText> { ["A"] = MarkupText.Plain("original") }])
		}, async scoped =>
		{
			var result = await new SpeechService(Attributes, new FixedOptions(Options)).TransformAsync(scoped, await Node(actor.DbRef), MarkupText.Plain("body"), "|");
			await Assert.That(result.Message!.ToPlainText()).IsEqualTo($"#{actor.DbRef.Number}|{actor.Name}|changed|body");
			await Assert.That(scoped.CurrentState.Enactor).IsEqualTo(enactor.DbRef);
			await Assert.That(scoped.CurrentState.Registers.First()["A"].ToPlainText()).IsEqualTo("original");
			return result;
		});
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task SpeechAdmissionPrecedesSingleTransformation(bool denied)
	{
		var actor = await Player();
		var witness = await Player();
		var room = await Room(actor, witness);
		var marker = $"once_{Guid.NewGuid():N}";
		await Admin($"&SPEECHMOD {actor.DbRef}=[pemit({witness.DbRef},{marker})]%0");
		if (denied) await Admin($"@lock/speech {room}=#FALSE");
		await Command(actor, "say hello");
		await Assert.That(Factory.Notifications.For(witness.DbRef).Count(message => message == marker)).IsEqualTo(denied ? 0 : 1);
		await Assert.That(Factory.Notifications.For(actor.DbRef).Contains("You say, \"hello\"")).IsEqualTo(!denied);
	}

	[Test]
	public async Task SpoofedImmediateEmitUsesExecutorSpeechModAndSpeakerAdmission()
	{
		var actor = await Player();
		var speaker = await Player();
		var room = await Room(actor, speaker);
		await Admin($"@power {actor.DbRef}=Can_Spoof");
		await Admin($"@lock/speech {room}=={speaker.DbRef}");
		await Admin($"&SPEECHMOD {actor.DbRef}=actor %0 %#");
		await Admin($"&SPEECHMOD {speaker.DbRef}=speaker %0");
		await Factory.CommandParserFor(actor.DbRef, actor.Handle).With(state => state with { Enactor = speaker.DbRef },
			parser => parser.CommandParse(MarkupText.Plain("@emit/spoof original")));
		await Assert.That(Factory.Notifications.For(room).Contains($"actor original #{actor.DbRef.Number}")).IsTrue();
	}

	[Test]
	[Arguments("@pemit/silent")]
	[Arguments("@remit/silent")]
	[Arguments("@lemit")]
	public async Task NonImmediateEmitDoesNotRunSpeechMod(string command)
	{
		var actor = await Player();
		var room = await Room(actor);
		await Admin($"&SPEECHMOD {actor.DbRef}=modified");
		var target = command.StartsWith("@pemit", StringComparison.Ordinal) ? actor.DbRef : room;
		await Command(actor, command == "@lemit" ? "@lemit original" : $"{command} {target}=original");
		await Assert.That(Factory.Notifications.For(actor.DbRef).Contains("original")).IsTrue();
		await Assert.That(Factory.Notifications.For(actor.DbRef).Contains("modified")).IsFalse();
	}

	[Test]
	public async Task SpeechModInvocationLimitRemainsAFailure()
	{
		var actor = await Player();
		await Room(actor);
		await Admin($"&SPEECHMOD {actor.DbRef}=[add(1,2)][add(3,4)]");
		var parser = ConfiguredParser(actor, Options with { Limit = Options.Limit with { FunctionInvocationLimit = 1 } });
		var result = await parser.CommandParse(actor.Handle, Connections, MarkupText.Plain("@emit original"));
		await Assert.That(result.HadErrors).IsTrue();
		await Assert.That(Factory.Notifications.For(actor.DbRef).Contains("original")).IsFalse();
	}

	[Test]
	public async Task MalformedSpeechModDoesNotBecomeSuccessfulOriginalSpeech()
	{
		var actor = await Player();
		await Room(actor);
		await Admin($"&SPEECHMOD {actor.DbRef}=add(1,2");
		var result = await Command(actor, "@emit original");
		await Assert.That(result.HadErrors).IsTrue();
		await Assert.That(Factory.Notifications.For(actor.DbRef).Contains("original")).IsFalse();
	}

	[Test]
	public async Task SpeechModCannotEscapeEvaluationRestrictions()
	{
		var actor = await Player();
		var node = await Node(actor.DbRef);
		await Admin($"&SPEECHMOD {actor.DbRef}=modified");
		using var restrictions = new EvaluationRestrictions([]).Enter();
		var refused = false;
		try
		{
			await new SpeechService(Attributes, new FixedOptions(Options)).TransformAsync(Factory.CommandParserFor(actor.DbRef, actor.Handle), node, MarkupText.Plain("body"), "|");
		}
		catch (RestrictedExpressionException) { refused = true; }
		await Assert.That(refused).IsTrue();
	}

	[Test]
	public async Task SpeechModPropagatesCancellation()
	{
		var actor = await Player();
		var node = await Node(actor.DbRef);
		using var cancel = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancel.Token);
		using var scope = budget.Enter();
		cancel.Cancel();
		var cancelled = false;
		try
		{
			await new SpeechService(Attributes, new FixedOptions(Options)).TransformAsync(Factory.CommandParserFor(actor.DbRef, actor.Handle), node, MarkupText.Plain("body"), "|");
		}
		catch (OperationCanceledException) { cancelled = true; }
		await Assert.That(cancelled).IsTrue();
	}

	[Test]
	public async Task SpeechModRetainsStyledBody()
	{
		var actor = await Player();
		await Admin($"&SPEECHMOD {actor.DbRef}=%0");
		var body = (await Factory.CommandParser.FunctionParse(MarkupText.Plain("[ansi(r,hello)]")))!.Message!;
		var result = await new SpeechService(Attributes, new FixedOptions(Options)).TransformAsync(
			Factory.CommandParserFor(actor.DbRef, actor.Handle), await Node(actor.DbRef), body, "|");
		await Assert.That(result.Message!.Render(global::MarkupString.MarkupFormat.Ansi)).IsEqualTo(body.Render(global::MarkupString.MarkupFormat.Ansi));
	}

	[Test]
	[Arguments("say")]
	[Arguments("pose")]
	[Arguments("semipose")]
	public async Task LocalSpeechUsesFailureAttributesAndLoudBypass(string command)
	{
		var actor = await Player();
		var room = await Room(actor);
		var refusal = $"refused_{Guid.NewGuid():N}";
		await Admin($"&SPEECH_LOCK`FAILURE {room}={refusal}");
		await Admin($"@lock/speech {room}=#FALSE");
		await Command(actor, $"{command} hello");
		await Assert.That(Factory.Notifications.For(actor.DbRef).Contains(refusal)).IsTrue();
		await Flag(actor.DbRef, "LOUD");
		await Command(actor, $"{command} hello");
		var expected = command == "say" ? $"{actor.Name} says, \"hello\"" : command == "pose" ? $"{actor.Name} hello" : $"{actor.Name}hello";
		await Assert.That(Factory.Notifications.For(room).Contains(expected)).IsTrue();
	}
}
