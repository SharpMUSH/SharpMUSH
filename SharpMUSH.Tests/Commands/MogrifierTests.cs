using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@channel/mogrifier</c> and the individual <c>MOGRIFY`*</c> verbs.
///
/// <para>The command half is about what the channel record ends up holding and who is allowed to
/// change it; the verb half is about what each verb does to a line. The surrounding delivery
/// pipeline — which arguments each callback is handed, who it runs as, what reaches each member,
/// what is buffered — is <see cref="Integration.MogrifierIntegrationTests"/>.</para>
///
/// <para>Every verb here was inert before the lane-K fix: <c>EvaluateMogrifyAttribute</c> passed a
/// null parser, <c>AttributeService</c> dereferenced it immediately, and the bare catch turned the
/// <c>NullReferenceException</c> into "this mogrifier said nothing".</para>
/// </summary>
public class MogrifierTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private static string UniqueChannel(string prefix)
		=> TestIsolationHelpers.GenerateUniqueName(prefix).Replace("_", string.Empty);

	private async Task<string> AsGod(string command)
		=> (await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)))
			.Message?.ToPlainText() ?? string.Empty;

	private async Task As(TestIsolationHelpers.TestPlayer who, string command)
		=> await GodParser.CommandParse(who.Handle, ConnectionService, MarkupText.Plain(command));

	private async Task<TestIsolationHelpers.TestPlayer> Mortal(string prefix)
		=> await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private async Task<SharpChannel> Channel(string name)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		await Mediator.Send(new CreateChannelCommand(MarkupText.Plain(name), ["Player"], god));
		return (await Mediator.Send(new GetChannelQuery(name)))!;
	}

	/// <summary>
	/// What the channel records as its mogrifier. <c>@channel/mogrifier</c> stores whatever
	/// <c>LocateService</c> resolved, and <c>DBRef.ToString()</c> is an objid — <c>#N:&lt;created&gt;</c> —
	/// so the dbref is the part before the colon.
	/// </summary>
	private async Task<string?> MogrifierOf(string channelName)
		=> (await Mediator.Send(new GetChannelQuery(channelName)))?.Mogrifier?.Split(':')[0];

	private async Task Join(SharpChannel channel, DBRef who)
	{
		var obj = (await Mediator.Send(new GetObjectNodeQuery(who))).Expect<AnySharpObject>();
		await Mediator.Send(new AddUserToChannelCommand(channel, obj));
	}

	private async Task<string> Thing(string prefix)
	{
		var dbref = await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, prefix);
		return $"#{dbref.Number}";
	}

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	/// <summary>A channel with a mortal speaker and a mortal listener on it, plus a mogrifier object.</summary>
	private sealed record Stage(
		string ChannelName,
		SharpChannel Channel,
		TestIsolationHelpers.TestPlayer Speaker,
		TestIsolationHelpers.TestPlayer Listener,
		string Mogrifier);

	private async Task<Stage> Setup(string prefix, bool attachMogrifier = true)
	{
		var name = UniqueChannel(prefix);
		var channel = await Channel(name);
		var speaker = await Mortal($"{prefix}Speaker");
		var listener = await Mortal($"{prefix}Listener");
		await Join(channel, speaker.DbRef);
		await Join(channel, listener.DbRef);

		var mogrifier = await Thing($"{prefix}Mog");
		if (attachMogrifier)
		{
			await AsGod($"@channel/mogrifier {name}={mogrifier}");
		}

		return new Stage(name, (await Mediator.Send(new GetChannelQuery(name)))!, speaker, listener, mogrifier);
	}

	// --- The command --------------------------------------------------------------------------------

	/// <summary>
	/// <c>@channel/mogrifier &lt;channel&gt;=&lt;object&gt;</c> records the object's dbref on the
	/// channel and says so.
	/// </summary>
	[Test]
	public async Task ChannelMogrifier_SetCommand_RecordsObjectOnChannel()
	{
		var name = UniqueChannel("MogSet");
		await Channel(name);
		var mogrifier = await Thing("MogSetTarget");

		var said = await AsGod($"@channel/mogrifier {name}={mogrifier}");

		await Assert.That(await MogrifierOf(name)).IsEqualTo(mogrifier);
		await Assert.That(said).Contains("Mogrifier has been updated");
	}

	/// <summary>
	/// The same switch with no object clears the mogrifier back to unset, which is what turns
	/// mogrification off without deleting the object.
	/// </summary>
	[Test]
	public async Task ChannelMogrifier_ClearCommand_UnsetsMogrifier()
	{
		var name = UniqueChannel("MogClear");
		await Channel(name);
		var mogrifier = await Thing("MogClearTarget");
		await AsGod($"@channel/mogrifier {name}={mogrifier}");
		await Assert.That(await MogrifierOf(name)).IsEqualTo(mogrifier);

		var said = await AsGod($"@channel/mogrifier {name}");

		await Assert.That(await MogrifierOf(name)).IsEqualTo(string.Empty);
		await Assert.That(said).Contains("Mogrifier has been cleared");
	}

	/// <summary>
	/// Setting a mogrifier is a channel modification, so a member who does not control the channel
	/// cannot do it. The actor is a mortal on purpose: the fixtures otherwise run as God, and handle 1
	/// controls everything, so this gate would pass vacuously.
	/// </summary>
	[Test]
	public async Task ChannelMogrifier_MortalNonOwner_CannotSetMogrifier()
	{
		var name = UniqueChannel("MogPerm");
		var channel = await Channel(name);
		var mortal = await Mortal("MogPermMortal");
		await Join(channel, mortal.DbRef);
		var mogrifier = await Thing("MogPermTarget");

		await As(mortal, $"@channel/mogrifier {name}={mogrifier}");

		await Assert.That(await MogrifierOf(name)).IsEqualTo(string.Empty);
	}

	// --- The verbs ----------------------------------------------------------------------------------

	/// <summary>
	/// With no mogrifier attached, a channel line is the plain PennMUSH rendering:
	/// <c>&lt;Channel&gt; Name says, "text"</c>.
	/// </summary>
	[Test]
	public async Task ChannelMessage_WithoutMogrifier_SendsBasicFormat()
	{
		var stage = await Setup("MogNone", attachMogrifier: false);

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=plain and simple"));

		await Assert.That(heard).Contains(x =>
			x.Contains($"<{stage.ChannelName}> {stage.Speaker.Name} says, \"plain and simple\""));
	}

	/// <summary>
	/// <c>MOGRIFY`BLOCK</c> blocks on any non-empty result, and only on a non-empty one — an attribute
	/// that evaluates to nothing is not a refusal, it is a mogrifier that decided not to interfere.
	/// </summary>
	[Test]
	public async Task MogrifyBlock_NonEmpty_BlocksMessage()
	{
		var stage = await Setup("MogBlockVerb");

		// An empty result is not a block: the line goes out normally.
		await AsGod($"&MOGRIFY`BLOCK {stage.Mogrifier}=[if(strmatch(%2,forbidden*),Refused: %2)]");

		var allowed = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=allowed text"));
		await Assert.That(allowed).Contains(x => x.Contains("allowed text"));

		// A non-empty result is, and its text is what the speaker is told.
		var blocked = await MessagesWhile(stage.Listener.DbRef, async () =>
		{
			var told = await MessagesWhile(stage.Speaker.DbRef,
				() => As(stage.Speaker, $"@chat {stage.ChannelName}=forbidden text"));
			await Assert.That(told).Contains(x => x.Contains("Refused: forbidden text"));
		});

		await Assert.That(blocked).DoesNotContain(x => x.Contains("forbidden text"));
	}

	/// <summary>
	/// <c>MOGRIFY`OVERRIDE</c> suppresses the members' own <c>@chatformat</c> and nothing else — the
	/// mogrifier's own <c>MOGRIFY`FORMAT</c> still runs, because the two are not alternatives.
	/// </summary>
	[Test]
	public async Task MogrifyOverride_True_SkipsChatFormat()
	{
		var stage = await Setup("MogOverrideVerb");
		await AsGod($"&MOGRIFY`OVERRIDE {stage.Mogrifier}=1");
		await AsGod($"&MOGRIFY`FORMAT {stage.Mogrifier}=MOG| %2");
		await AsGod($"&CHATFORMAT {stage.Listener.DbRef}=FMT| %5");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=override wins"));

		await Assert.That(heard).Contains(x => x.Contains("MOG| override wins"));
		await Assert.That(heard).DoesNotContain(x => x.Contains("FMT|"));
	}

	/// <summary>
	/// <c>MOGRIFY`FORMAT</c> is channel-wide: the speaker sees the same rewritten line as every other
	/// member, because it replaces the message the channel sends rather than any one member's view.
	/// </summary>
	[Test]
	public async Task MogrifyFormat_CustomFormat_AltersMessage()
	{
		var stage = await Setup("MogFormatVerb");
		await AsGod($"&MOGRIFY`FORMAT {stage.Mogrifier}=-- %3 -- %2 --");

		var mine = new List<string>();
		var theirs = await MessagesWhile(stage.Listener.DbRef, async () =>
			mine = await MessagesWhile(stage.Speaker.DbRef,
				() => As(stage.Speaker, $"@chat {stage.ChannelName}=everyone alike")));

		var expected = $"-- {stage.Speaker.Name} -- everyone alike --";
		await Assert.That(mine).Contains(x => x.Contains(expected));
		await Assert.That(theirs).Contains(x => x.Contains(expected));
	}

	/// <summary>
	/// <c>MOGRIFY`TITLE</c> rewrites the speaker's channel title, which sits between the channel name
	/// and the speaker's name in the default rendering.
	/// </summary>
	[Test]
	public async Task MogrifyParts_CustomValues_AlterComponents()
	{
		var stage = await Setup("MogTitleVerb");
		await As(stage.Speaker, $"@channel/title {stage.ChannelName}=Squire");
		await AsGod($"&MOGRIFY`TITLE {stage.Mogrifier}=[ucstr(%0)] of the Realm");

		var heard = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=titled"));

		await Assert.That(heard).Contains(x =>
			x.Contains($"<{stage.ChannelName}> SQUIRE of the Realm {stage.Speaker.Name} says, \"titled\""));
	}

	/// <summary>
	/// The mogrifier's <c>@lock/use</c> decides per speaker, not once for the channel: the same
	/// mogrifier rewrites the line for a speaker who passes the lock and leaves it alone for one who
	/// does not. Both speakers are mortals — handle 1 is a wizard and passes every lock vacuously.
	/// </summary>
	[Test]
	public async Task MogrifyUseLock_Fails_SkipsMogrification()
	{
		var stage = await Setup("MogLockVerb");
		var permitted = await Mortal("MogLockPermitted");
		await Join(stage.Channel, permitted.DbRef);

		await AsGod($"&MOGRIFY`FORMAT {stage.Mogrifier}=MOG| %2");
		await AsGod($"@lock/use {stage.Mogrifier}=#{permitted.DbRef.Number}");

		var fromPermitted = await MessagesWhile(stage.Listener.DbRef,
			() => As(permitted, $"@chat {stage.ChannelName}=I may mogrify"));
		await Assert.That(fromPermitted).Contains(x => x.Contains("MOG| I may mogrify"));

		var fromRefused = await MessagesWhile(stage.Listener.DbRef,
			() => As(stage.Speaker, $"@chat {stage.ChannelName}=I may not"));
		await Assert.That(fromRefused).DoesNotContain(x => x.Contains("MOG|"));
		await Assert.That(fromRefused).Contains(x => x.Contains($"says, \"I may not\""));
	}
}
