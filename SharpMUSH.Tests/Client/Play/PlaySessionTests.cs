using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Services.Play;
using SharpMUSH.Library.Models.Portal.Play;

namespace SharpMUSH.Tests.Client.Play;

/// <summary>
/// The tab's play session as an application sees it: the packages it asked for, in order, each handler on its own;
/// a reset whenever what it held belongs to someone or something else.
/// </summary>
public class PlaySessionTests
{
	private const string Character = "#12:1700000000000";

	private sealed class Harness
	{
		public readonly List<ITerminalService> Terminals = [];
		public readonly TerminalServiceHost Host;
		public readonly PlaySession Session;
		public bool SignedIn = true;
		public int Connects;

		public Harness()
		{
			Host = new TerminalServiceHost(() =>
			{
				var terminal = Substitute.For<ITerminalService>();
				terminal.OobChannels.Returns(new OobChannelStore());
				Terminals.Add(terminal);
				return terminal;
			});
			Session = new PlaySession(Host, () =>
			{
				Connects++;
				return Task.CompletedTask;
			}, () => SignedIn, NullLogger<PlaySession>.Instance);
		}

		public ITerminalService Terminal => Terminals[^1];

		public IOobChannelStore Store => Terminal.OobChannels;

		public void Connect(string character = Character, string name = "Tamsin")
		{
			Terminal.Identity.Returns(new TerminalIdentity("account", character));
			Terminal.ConnectedPlayerName.Returns(name);
			Terminal.IsConnected.Returns(true);
			Terminal.ConnectionStateChanged += Raise.Event<Action<bool>>(true);
		}

		public void Drop()
		{
			Terminal.IsConnected.Returns(false);
			Terminal.ConnectionStateChanged += Raise.Event<Action<bool>>(false);
		}

		public List<PlayEvent> Listen(PlaySubscription subscription, IPlaySession? through = null)
		{
			var heard = new List<PlayEvent>();
			(through ?? Session).Subscribe(subscription, item =>
			{
				heard.Add(item);
				return Task.CompletedTask;
			});
			return heard;
		}
	}

	private static PlaySubscription For(params string[] packages) => new() { Packages = packages };

	private static IEnumerable<PlayPackage> Packages(IEnumerable<PlayEvent> heard) => heard.OfType<PlayPackage>();

	[Test]
	public async Task ASubscriptionHearsOnlyThePackagesItNames()
	{
		var harness = new Harness();
		harness.Connect();
		var heard = harness.Listen(For("myapp.*", "other.exact"));

		harness.Store.Receive(new OobFrame("myapp.event", """{"v":2,"n":1}""", "#3:1"));
		harness.Store.Receive(new OobFrame("other.exact", "{}", "#3:1"));
		harness.Store.Receive(new OobFrame("other.more", "{}", "#3:1"));
		harness.Store.Receive(new OobFrame("unrelated", "{}", "#3:1"));

		await Assert.That(Packages(heard).Select(p => p.Package)).IsEquivalentTo(["myapp.event", "other.exact"]);
		var first = Packages(heard).First();
		await Assert.That(first.Version).IsEqualTo(2);
		await Assert.That(first.Origin).IsEqualTo(PlayOrigin.Live);
		await Assert.That(first.Sender).IsEqualTo(PlaySender.Other);
		await Assert.That(first.SenderObjId).IsEqualTo("#3:1");
		await Assert.That(first.Data.GetProperty("n").GetInt32()).IsEqualTo(1);
	}

	[Test]
	public async Task QueryAnswersAndTheEnginesOwnPackagesAreNeverDelivered()
	{
		var harness = new Harness();
		harness.Connect();
		var heard = harness.Listen(For("query.*", "sharp.*"));

		harness.Store.Receive(new OobFrame("query.42", "\"answer\""));
		harness.Store.Receive(new OobFrame("sharp.play", """{"v":1,"character":"#12:1700000000000","name":"Tamsin","room":"#0:1"}"""));

		await Assert.That(Packages(heard)).IsEmpty();
	}

	[Test]
	public async Task ThePlayersOwnSoftcodeIsHeardOnlyWhenAskedFor()
	{
		var harness = new Harness();
		harness.Connect();
		var strict = harness.Listen(For("myapp.*"));
		var open = harness.Listen(For("myapp.*") with { AcceptSelfSent = true });

		harness.Store.Receive(new OobFrame("myapp.event", "{}", Character));

		await Assert.That(Packages(strict)).IsEmpty();
		await Assert.That(Packages(open).Single().Sender).IsEqualTo(PlaySender.Self);
	}

	[Test]
	public async Task APackageFromAnUnnamedSenderIsDelivered()
	{
		var harness = new Harness();
		harness.Connect();
		var heard = harness.Listen(For("myapp.*"));

		harness.Store.Set("myapp.event", "{}");

		await Assert.That(Packages(heard).Single().Sender).IsEqualTo(PlaySender.Unknown);
	}

	[Test]
	public async Task CatchUpAfterAResumeIsMarked()
	{
		var harness = new Harness();
		harness.Connect();
		var heard = harness.Listen(For("myapp.*"));

		harness.Store.Receive(new OobFrame("myapp.event", "{}", "#3:1", CatchUp: true));

		await Assert.That(Packages(heard).Single().Origin).IsEqualTo(PlayOrigin.CatchUp);
	}

	[Test]
	public async Task LatestStartsWithTheCachedValueAndEachDoesNot()
	{
		var harness = new Harness();
		harness.Connect();
		harness.Store.Receive(new OobFrame("myapp.state", """{"hp":5}""", "#3:1"));
		harness.Store.Receive(new OobFrame("myapp.state", """{"hp":4}""", "#3:1"));

		var latest = harness.Listen(For("myapp.*") with { Delivery = PlayDelivery.Latest });
		var each = harness.Listen(For("myapp.*"));

		var cached = Packages(latest).Single();
		await Assert.That(cached.Origin).IsEqualTo(PlayOrigin.Cached);
		await Assert.That(cached.Data.GetProperty("hp").GetInt32()).IsEqualTo(4);
		await Assert.That(Packages(each)).IsEmpty();
	}

	[Test]
	public async Task AHandlerThatThrowsStillHearsTheNextEvent()
	{
		var harness = new Harness();
		harness.Connect();
		var calls = 0;
		harness.Session.Subscribe(For("myapp.*"), _ =>
		{
			calls++;
			throw new InvalidOperationException("bad payload");
		});

		harness.Store.Receive(new OobFrame("myapp.a", "{}"));
		harness.Store.Receive(new OobFrame("myapp.b", "{}"));

		await Assert.That(calls).IsEqualTo(2);
	}

	[Test]
	public async Task AHandlerGetsOneEventAtATimeInOrder()
	{
		var harness = new Harness();
		harness.Connect();
		var gate = new TaskCompletionSource();
		var heard = new List<string>();
		harness.Session.Subscribe(For("myapp.*"), async item =>
		{
			var package = ((PlayPackage)item).Package;
			heard.Add($"start {package}");
			if (package == "myapp.a") await gate.Task;
			heard.Add($"end {package}");
		});

		harness.Store.Receive(new OobFrame("myapp.a", "{}"));
		harness.Store.Receive(new OobFrame("myapp.b", "{}"));
		await Assert.That(heard).IsEquivalentTo(["start myapp.a"]);

		gate.SetResult();
		await Task.Yield();

		await Assert.That(heard).IsEquivalentTo(["start myapp.a", "end myapp.a", "start myapp.b", "end myapp.b"]);
	}

	[Test]
	public async Task TheStateFollowsTheConnection()
	{
		var harness = new Harness();
		await Assert.That(harness.Session.State.Connection).IsEqualTo(PlayConnection.NotPlaying);

		var heard = harness.Listen(For());
		harness.Connect();
		var connected = harness.Session.State;
		await Assert.That(connected.Connection).IsEqualTo(PlayConnection.Connected);
		await Assert.That(connected.Character).IsEqualTo(new PlayCharacter(Character, "Tamsin", false));

		harness.Drop();
		await Assert.That(harness.Session.State.Connection).IsEqualTo(PlayConnection.Reconnecting);
		await Assert.That(harness.Session.State.Character?.ObjId).IsEqualTo(Character);
		await Assert.That(heard.OfType<PlayStateChanged>().Select(c => c.Current.Connection))
			.IsEquivalentTo([PlayConnection.Connected, PlayConnection.Reconnecting]);
		await Assert.That(heard.OfType<PlayReset>()).IsEmpty();
	}

	[Test]
	public async Task TheEnginesPlayPackageNamesTheCharacterAndRoom()
	{
		var harness = new Harness();
		harness.Connect(name: "");
		var heard = harness.Listen(For());

		harness.Store.Receive(new OobFrame("sharp.play", """{"v":1,"character":"#12:1700000000000","name":"Tamsin","room":"#40:1"}"""));

		var state = harness.Session.State;
		await Assert.That(state.Character?.Name).IsEqualTo("Tamsin");
		await Assert.That(state.Room).IsEqualTo("#40:1");
		await Assert.That(heard.OfType<PlayStateChanged>().Single().Current.Room).IsEqualTo("#40:1");
	}

	[Test]
	public async Task AnotherCharacterOnTheSameConnectionResetsToTheNewOne()
	{
		var harness = new Harness();
		harness.Connect();
		var heard = harness.Listen(For());

		harness.Store.Receive(new OobFrame("sharp.play", """{"v":1,"character":"#77:1","name":"Other","room":"#41:1"}"""));

		await Assert.That(heard.OfType<PlayReset>().Single().Reason).IsEqualTo(PlayResetReason.CharacterChanged);
		var after = heard.OfType<PlayStateChanged>().First();
		await Assert.That(after.Current.Character?.ObjId).IsEqualTo("#77:1");
		await Assert.That(after.Current.Room).IsEqualTo("#41:1");
	}

	[Test]
	public async Task LoggingOutOnAnOpenConnectionEndsAndLoggingInAgainPlays()
	{
		var harness = new Harness();
		harness.Connect();
		var heard = harness.Listen(For());

		harness.Store.Receive(new OobFrame("sharp.play", """{"v":1,"character":null}"""));
		await Assert.That(harness.Session.State.Connection).IsEqualTo(PlayConnection.NotPlaying);
		await Assert.That(heard.OfType<PlayReset>().Single().Reason).IsEqualTo(PlayResetReason.Ended);

		harness.Store.Receive(new OobFrame("sharp.play", """{"v":1,"character":"#12:1700000000000","name":"Tamsin","room":"#40:1"}"""));
		await Assert.That(harness.Session.State.Connection).IsEqualTo(PlayConnection.Connected);
		await Assert.That(harness.Session.State.Room).IsEqualTo("#40:1");
		await Assert.That(heard.OfType<PlayReset>().Count()).IsEqualTo(1);
	}

	[Test]
	public async Task AClosedApplicationGetsNothingEvenCached()
	{
		var harness = new Harness();
		harness.Connect();
		harness.Store.Receive(new OobFrame("myapp.state", "{}"));
		var app = harness.Session.ForApplication("myapp", ["myapp.*"]);
		app.Dispose();

		var heard = harness.Listen(For("myapp.*") with { Delivery = PlayDelivery.Latest }, app);

		await Assert.That(heard).IsEmpty();
	}

	[Test]
	public async Task APlayPackageFromStaffSoftcodeIsRead()
	{
		var harness = new Harness();
		harness.Connect();

		harness.Store.Receive(new OobFrame("sharp.play", $$"""{"v":1,"character":"{{Character}}","name":"Tamsin","room":"#40:1"}""", "#1:1"));

		await Assert.That(harness.Session.State.Room).IsEqualTo("#40:1")
			.Because("the server sends sharp.* from softcode only for holders of oob.engine");
	}

	[Test]
	public async Task ACharacterSwitchResetsAndDropsTheCache()
	{
		var harness = new Harness();
		harness.Connect();
		harness.Store.Receive(new OobFrame("myapp.state", "{}", "#3:1"));
		var heard = harness.Listen(For("myapp.*"));

		await harness.Host.RecreateAsync();

		var reset = heard.OfType<PlayReset>().Single();
		await Assert.That(reset.Reason).IsEqualTo(PlayResetReason.CharacterChanged);
		await Assert.That(reset.Generation).IsEqualTo(1);
		await Assert.That(heard[^1] is PlayStateChanged { Generation: 1, Current.Connection: PlayConnection.NotPlaying }).IsTrue();
		await Assert.That(Packages(harness.Listen(For("myapp.*") with { Delivery = PlayDelivery.Latest }))).IsEmpty();
	}

	[Test]
	public async Task ARebuildAfterSigningOutSaysSo()
	{
		var harness = new Harness { SignedIn = false };
		harness.Connect();
		var heard = harness.Listen(For());

		await harness.Host.RecreateAsync();

		await Assert.That(heard.OfType<PlayReset>().Single().Reason).IsEqualTo(PlayResetReason.SignedOut);
	}

	[Test]
	public async Task SigningOutResetsOnceBeforeTheTerminalsHangUp()
	{
		var harness = new Harness();
		harness.Connect();
		var heard = harness.Listen(For());

		await harness.Session.OnAccountSessionEndingAsync();
		harness.Store.Signal(OobSessionSignal.Ended);
		harness.Drop();

		await Assert.That(heard.OfType<PlayReset>().Select(r => r.Reason)).IsEquivalentTo([PlayResetReason.SignedOut]);
		await Assert.That(harness.Session.State.Character).IsNull();
		await Assert.That(harness.Session.State.Connection).IsEqualTo(PlayConnection.NotPlaying);
	}

	[Test]
	public async Task AnEndedSessionResetsOnceAndIsNotPlaying()
	{
		var harness = new Harness();
		harness.Connect();
		var heard = harness.Listen(For());

		harness.Store.Signal(OobSessionSignal.Ended);
		harness.Store.Signal(OobSessionSignal.Ended);

		await Assert.That(heard.OfType<PlayReset>().Select(r => r.Reason)).IsEquivalentTo([PlayResetReason.Ended]);
		await Assert.That(harness.Session.State.Connection).IsEqualTo(PlayConnection.NotPlaying);
	}

	[Test]
	public async Task ARefusedResumeIsANewSessionForTheSameCharacter()
	{
		var harness = new Harness();
		harness.Connect();
		var heard = harness.Listen(For());

		harness.Store.Signal(OobSessionSignal.ResumeRefused);

		await Assert.That(heard.OfType<PlayReset>().Single().Reason).IsEqualTo(PlayResetReason.NewSession);
		await Assert.That(harness.Session.State.Character?.ObjId).IsEqualTo(Character);
	}

	[Test]
	public async Task EveryEventCarriesItsGeneration()
	{
		var harness = new Harness();
		harness.Connect();
		var heard = harness.Listen(For("myapp.*"));

		harness.Store.Receive(new OobFrame("myapp.a", "{}"));
		harness.Store.Signal(OobSessionSignal.ResumeRefused);
		harness.Store.Receive(new OobFrame("myapp.b", "{}"));

		await Assert.That(Packages(heard).Select(p => p.Generation)).IsEquivalentTo([0L, 1L]);
	}

	[Test]
	public async Task AnApplicationHearsOnlyWhatItDeclares()
	{
		var harness = new Harness();
		harness.Connect();
		var app = harness.Session.ForApplication("myapp", ["myapp.*"]);
		var heard = harness.Listen(For("myapp.*", "other.*"), app);

		harness.Store.Receive(new OobFrame("myapp.event", "{}"));
		harness.Store.Receive(new OobFrame("other.event", "{}"));

		await Assert.That(Packages(heard).Select(p => p.Package)).IsEquivalentTo(["myapp.event"]);
	}

	[Test]
	public async Task AnApplicationThatDeclaresNothingHearsOnlyTheLifecycle()
	{
		var harness = new Harness();
		harness.Connect();
		var app = harness.Session.ForApplication("myapp", []);
		var heard = harness.Listen(For("myapp.*"), app);

		harness.Store.Receive(new OobFrame("myapp.event", "{}"));
		harness.Store.Signal(OobSessionSignal.Ended);

		await Assert.That(Packages(heard)).IsEmpty();
		await Assert.That(heard.OfType<PlayReset>()).IsNotEmpty();
	}

	[Test]
	public async Task ClosingTheApplicationEndsItsSubscriptions()
	{
		var harness = new Harness();
		harness.Connect();
		var app = harness.Session.ForApplication("myapp", ["myapp.*"]);
		var heard = harness.Listen(For("myapp.*"), app);

		app.Dispose();
		harness.Store.Receive(new OobFrame("myapp.event", "{}"));

		await Assert.That(heard).IsEmpty();
	}

	[Test]
	public async Task HandlersRunThroughTheApplicationsDispatcher()
	{
		var harness = new Harness();
		harness.Connect();
		var dispatched = 0;
		var app = harness.Session.ForApplication("myapp", ["myapp.*"], work =>
		{
			dispatched++;
			return work();
		});
		harness.Listen(For("myapp.*"), app);

		harness.Store.Receive(new OobFrame("myapp.event", "{}"));

		await Assert.That(dispatched).IsEqualTo(1);
	}

	[Test]
	public async Task ConnectingOpensTheConnectionOnlyWhenItIsNotOpen()
	{
		var harness = new Harness();

		await harness.Session.ConnectAsync();
		harness.Connect();
		await harness.Session.ConnectAsync();

		await Assert.That(harness.Connects).IsEqualTo(1);
	}
}
