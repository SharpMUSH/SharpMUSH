using MarkupString;
using MarkupString.Ansi;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public partial class PrivateListenerTests
{
	[Test]
	[Arguments("command")]
	[Arguments("listen")]
	[Arguments("monitor")]
	public async Task EscapedPatternsPreserveStyledCapturesAtDiscoveryAndAdmission(string surface)
	{
		var actor = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "EscapedPattern");
		var node = await Node(listener);
		var attributes = Factory.Services.GetRequiredService<IAttributeService>();
		const string pattern = @"hello \a\\*\?";
		const string action = "&RESULT me=%0";
		if (surface == "monitor") await Admin($"@set {listener}=MONITOR");
		await attributes.SetAttributeAsync(await Node(new DBRef(1)), node, surface == "listen" ? "LISTEN" : "PATTERN",
			MarkupText.Plain(surface == "listen" ? pattern : (surface == "command" ? "$" : "^") + pattern + ":" + action));
		if (surface == "listen") await attributes.SetAttributeAsync(await Node(new DBRef(1)), node, "AHEAR", MarkupText.Plain(action));
		var pipeline = await Build(actor, actor.DbRef);
		var red = AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false));
		var input = MarkupText.Concat([MarkupText.Plain(@"hello a\"), MarkupText.Wrap(red, "one\ntwo"), MarkupText.Plain("?")]);
		if (surface == "command")
		{
			var discovery = Factory.Services.GetRequiredService<ICommandDiscoveryService>();
			var matches = (await discovery.MatchUserDefinedCommand(pipeline.Parser, new[] { node }.ToAsyncEnumerable(), input))
				.Expect<IEnumerable<(AnySharpObject SObject, SharpAttribute Attribute, Dictionary<string, CallState> Arguments)>>();
			var capture = matches.Single().Arguments["0"].Message!;
			await Assert.That(capture.ToPlainText()).IsEqualTo("one\ntwo");
			await Assert.That(capture.Runs.Single().Markups.Single()).IsEqualTo(red);
			var rejected = await discovery.MatchUserDefinedCommand(pipeline.Parser, new[] { node }.ToAsyncEnumerable(), MarkupText.Concat(input, MarkupText.Plain("\n")));
			await Assert.That(rejected is IEnumerable<(AnySharpObject, SharpAttribute, Dictionary<string, CallState>)> found && found.Any()).IsFalse();
		}
		else
		{
			await pipeline.Notify.Notify(listener, input, await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
			var queued = pipeline.Queue.Single();
			await Assert.That(queued.State.EnvironmentRegisters["0"].Message!.ToPlainText()).IsEqualTo("one\ntwo");
			await Assert.That(queued.State.EnvironmentRegisters["0"].Message!.Runs.Single().Markups.Single()).IsEqualTo(red);
			await pipeline.Parser.FromState(queued.State).CommandListParse(queued.Command);
			var result = (await attributes.GetAttributeAsync(node, node, "RESULT", IAttributeService.AttributeMode.Read, false))
				.Expect<SharpAttribute[]>().Last().Value;
			await Assert.That(result.ToPlainText()).IsEqualTo("one\ntwo");
			pipeline.Queue.Clear();
			await pipeline.Notify.Notify(listener, MarkupText.Concat(input, MarkupText.Plain("\n")), await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
			await Assert.That(pipeline.Queue).IsEmpty();
		}
	}

	[Test]
	[Arguments(@"strmatch(ab,a\\b)", "1")]
	[Arguments(@"grab(ab ax,a\\b)", "ab")]
	[Arguments("strmatch(cat%r,cat)", "0")]
	public async Task WildcardFunctionsUseEscapesAndWholeInput(string expression, string expected)
	{
		var result = await Factory.FunctionParser.FunctionParse(MarkupText.Plain(expression));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}
}
