using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public partial class SpeechTransformationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private readonly List<long> _handles = [];
	private async Task<TestIsolationHelpers.TestPlayer> Player()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "SpeechTransform");
		_handles.Add(player.Handle);
		return player;
	}
	[After(Test)]
	public async Task DisconnectPlayers()
	{
		foreach (var handle in _handles) await Connections.Disconnect(handle);
	}
	private async Task<CallState> Admin(string command) => await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(command));
	private async Task<CallState> Command(TestIsolationHelpers.TestPlayer actor, string command)
		=> await Factory.CommandParserFor(actor.DbRef, actor.Handle).CommandParse(actor.Handle, Connections, MarkupText.Plain(command));
	private async Task<DBRef> Room(params TestIsolationHelpers.TestPlayer[] players)
	{
		var room = DBRef.Parse((await Admin($"@dig {Guid.NewGuid():N}")).Message!.ToPlainText().Trim());
		foreach (var player in players) await Admin($"@tel {player.DbRef}={room}");
		return room;
	}

	[Test]
	[Arguments("say", "\"", "You say, \"\"|hello\"")]
	[Arguments("pose", ":", "Joe :|hello")]
	[Arguments("semipose", ";", "Joe;|hello")]
	[Arguments("@emit", "|", "||hello")]
	public async Task SpeechModUsesTheOriginalBodyAndSpeechToken(string command, string token, string selfMessage)
	{
		var actor = await Player();
		var witness = await Player();
		var room = await Room(actor, witness);
		await Admin($"&SPEECHMOD {actor.DbRef}=%1|%0");
		await Command(actor, $"{command} hello");
		selfMessage = selfMessage.Replace("Joe", actor.Name, StringComparison.Ordinal);
		await Assert.That(Factory.Notifications.For(actor.DbRef).Contains(selfMessage)).IsTrue();
		var audience = command == "say" ? $"{actor.Name} says, \"{token}|hello\"" : selfMessage;
		await Assert.That(Factory.Notifications.For(witness.DbRef).Contains(audience)).IsTrue();
		await Assert.That(Factory.Notifications.For(room).Contains(audience)).IsTrue();
	}

	[Test]
	public async Task SayStripsConfiguredInitialQuoteBeforeSpeechMod()
	{
		var actor = await Player();
		await Room(actor);
		await Admin($"&SPEECHMOD {actor.DbRef}=modified %0");
		await Command(actor, "say \"hello");
		await Assert.That(Factory.Notifications.For(actor.DbRef).Contains("You say, \"modified hello\"")).IsTrue();
	}

	[Test]
	public async Task NoSpacePosePreservesBodyLeadingSpaces()
	{
		var actor = await Player();
		await Room(actor);
		await Command(actor, "pose/nospace %b%bhello");
		await Assert.That(Factory.Notifications.For(actor.DbRef).Contains($"{actor.Name}  hello")).IsTrue();
	}

	[Test]
	public async Task ExplicitAccentAndMonikerUseTheActualName()
	{
		var actor = await Player();
		var suffix = Guid.NewGuid().ToString("N");
		var name = "Joe" + suffix;
		await Admin($"@name {actor.DbRef}={name}");
		await Admin($"&NAMEACCENT {actor.DbRef}=-'-{new string('-', suffix.Length)}");
		await Admin($"@moniker {actor.DbRef}=[ansi(r,xx)]");
		var parser = Factory.CommandParserFor(actor.DbRef, actor.Handle);
		var accented = (await parser.FunctionParse(MarkupText.Plain("%~")))!;
		await Assert.That(accented.Message!.ToPlainText()).IsEqualTo("Jóe" + suffix);
		var moniker = (await parser.FunctionParse(MarkupText.Plain("%k")))!;
		await Assert.That(moniker.Message!.ToPlainText()).IsEqualTo(name);
		await Assert.That(moniker.Message.Render(global::MarkupString.MarkupFormat.Ansi)).IsNotEqualTo(name);
		var function = (await parser.FunctionParse(MarkupText.Plain($"[moniker({actor.DbRef})]")))!;
		await Assert.That(function.Message!.ToPlainText()).IsEqualTo("Jóe" + suffix);
	}
}
