using MarkupString;
using MarkupString.Ansi;
using NSubstitute;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Services;

public partial class PrivateListenerTests
{
	[Test]
	[Arguments("pemit", false, false)]
	[Arguments("nspemit", false, false)]
	[Arguments("prompt", false, false)]
	[Arguments("nsprompt", false, false)]
	[Arguments("pemit", true, false)]
	[Arguments("nspemit", true, false)]
	[Arguments("prompt", true, false)]
	[Arguments("nsprompt", true, false)]
	[Arguments("pemit", false, true)]
	[Arguments("nspemit", false, true)]
	[Arguments("prompt", false, true)]
	[Arguments("nsprompt", false, true)]
	[Arguments("pemit", true, true)]
	[Arguments("nspemit", true, true)]
	[Arguments("prompt", true, true)]
	[Arguments("nsprompt", true, true)]
	public async Task PrivatePuppetRelayIsForcedAndNeverPropagatesPromptFraming(string name, bool function, bool remoteOwner)
	{
		var actor = await Player();
		var owner = await Player();
		var puppet = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "PrivatePuppet");
		var room = DBRef.Parse((await Admin($"@dig {Guid.NewGuid():N}")).Message!.ToPlainText().Trim());
		await Admin($"@tel {puppet}={room}");
		if (!remoteOwner) await Admin($"@tel {owner.DbRef}={room}");
		await Admin($"@chown {puppet}={owner.DbRef}");
		await Admin($"@set {puppet}=PUPPET");
		if (name.StartsWith("ns", StringComparison.Ordinal)) await Admin($"@power {actor.DbRef}=Can_Spoof");
		var pipeline = await Build(actor, owner.DbRef);
		var body = MarkupText.Wrap(AnsiMarkup.Create(foreground: System.Drawing.Color.Red.ToAnsiColor()), MarkupText.Plain("puppet body"));
		var invocation = function ? MarkupText.Concat([MarkupText.Plain($"[{name}({puppet},"), body, MarkupText.Plain(")]")])
			: MarkupText.Concat(MarkupText.Plain($"@{name}/silent {puppet}="), body);
		if (function) await pipeline.Parser.FunctionParse(invocation);
		else await pipeline.Parser.CommandParse(actor.Handle, Connections, invocation);
		var output = pipeline.Bus.ReceivedCalls().SelectMany(call => call.GetArguments().OfType<MarkupOutputMessage>()).Single(message => message.Handle == 901);
		var delivered = MarkupTextSerializer.Deserialize(output.Markup);
		await Assert.That(delivered.ToPlainText()).EndsWith("> puppet body");
		await Assert.That(delivered.Render(MarkupFormat.Ansi)).Contains("\u001b[");
		await Assert.That(pipeline.Bus.ReceivedCalls().SelectMany(call => call.GetArguments().OfType<MarkupPromptMessage>())).IsEmpty();
	}

	[Test]
	public async Task NonemptyPromptUsesOnlyPromptConsumerWithoutOutputWrapping()
	{
		var actor = await Player();
		var pipeline = await Build(actor, actor.DbRef);
		await pipeline.Notify.Prompt(actor.DbRef, "prompt body", await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		var prompt = pipeline.Bus.ReceivedCalls().SelectMany(call => call.GetArguments().OfType<MarkupPromptMessage>()).Single();
		await AssertPromptConsumer(prompt, "prompt body");
	}
}
