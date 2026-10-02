using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Components.Scenes;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.BUnit.Components;

namespace SharpMUSH.Tests.BUnit.Components.Play;

/// <summary>
/// README §5.4 Story view: the scene's poses (the REST backlog, then the scene hub's events), each with
/// its author's portrait and colour. Portraits come from the room's occupant rows, else the directory,
/// and only ever for the name the pose shows: a persona never borrows its author's face.
/// </summary>
public class SceneStoryTests : TrackingBunitContext
{
	private readonly FakeSceneHub _hub = new();
	private readonly StoryApi _api = new();

	public SceneStoryTests()
	{
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		Services
			.AddSingleton(factory)
			.AddSingleton(sp => new SceneService(factory, TestAccountAuth.Of(sp)))
			.AddSingleton(new CharacterDirectoryService(factory, NullLogger<CharacterDirectoryService>.Instance))
			.AddSingleton<IConnectionStateService>(_hub)
			.AddSingleton<ISceneHubControl>(_hub)
			.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static readonly RoomOccupant Tomas = new("#312", "Tomas Reyes", "look #312", "#312:1", "player", "#ffb454",
		new ImageRef("/api/wiki-assets/t/tomas-room.jpg", null, null, null, null), "active", 60, true, false, []);

	private static readonly RoomOccupant Alice = new("#10", "Alice", "look #10", "#10:1", "player", "#aa77ff",
		new ImageRef("/api/wiki-assets/a/alice.jpg", null, null, null, null), "active", 0, true, false, []);

	private IRenderedComponent<SceneStory> RenderStory(IReadOnlyList<RoomOccupant>? occupants = null, Action<bool>? unavailable = null,
		string id = "42")
	{
		var cut = Render<SceneStory>(p => p
			.Add(x => x.SceneId, id)
			.Add(x => x.Occupants, occupants ?? [])
			.Add(x => x.ViewerName, "Ilsa Varn")
			.Add(x => x.LiveUnavailableChanged, u => unavailable?.Invoke(u)));
		return cut;
	}

	private static void WaitForRows(IRenderedComponent<SceneStory> cut, int count) =>
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".story-row").Count != count) throw new InvalidOperationException("rows not loaded yet");
		}, TimeSpan.FromSeconds(5));

	[Test]
	public async Task TheBacklog_Renders_AndTheSceneIsJoined()
	{
		var cut = RenderStory();
		WaitForRows(cut, 3);
		await Assert.That(_hub.Joined).IsEquivalentTo(new[] { "42" });
		await Assert.That(cut.FindAll(".story-name")[0].TextContent).IsEqualTo("Tomas Reyes");
	}

	[Test]
	public async Task ADownSceneConnection_IsReported_AndItsReturnReadsTheGapBack()
	{
		// The connection failed to start (or dropped): the join is recorded for later, nothing live arrives,
		// and the page must say so rather than look live. Its return reads the backlog again, because a
		// reconnect does not replay what was posted meanwhile.
		_hub.IsSceneLive = false;
		var down = new List<bool>();
		var cut = Render<SceneStory>(p => p
			.Add(x => x.SceneId, "42")
			.Add(x => x.ViewerName, "Ilsa Varn")
			.Add(x => x.LiveDownChanged, d => down.Add(d)));
		WaitForRows(cut, 3);
		cut.WaitForAssertion(() => { if (down.Count == 0) throw new InvalidOperationException("not reported yet"); }, TimeSpan.FromSeconds(5));
		await Assert.That(down).IsEquivalentTo(new[] { true });
		var reads = _api.PoseReads;

		await cut.InvokeAsync(() => _hub.SetSceneLive(true));
		cut.WaitForAssertion(() => { if (down.Count < 2) throw new InvalidOperationException("not reported yet"); }, TimeSpan.FromSeconds(5));
		await Assert.That(down[^1]).IsFalse();
		cut.WaitForAssertion(() => { if (_api.PoseReads <= reads) throw new InvalidOperationException("no catch-up read yet"); }, TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task AnOccupantRow_GivesThePortraitAndColour_ByTheAuthorsDbref()
	{
		var cut = RenderStory([Tomas]);
		WaitForRows(cut, 3);
		var first = cut.FindAll(".story-row")[0];
		await Assert.That(first.QuerySelector("img.story-portrait")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/t/tomas-room.jpg");
		await Assert.That(first.QuerySelector(".story-name")!.GetAttribute("style")).Contains("#ffb454");
	}

	[Test]
	public async Task WithoutAnOccupantRow_TheDirectoryGivesThePortrait()
	{
		var cut = RenderStory();
		cut.WaitForAssertion(() => cut.Find(".story-row img.story-portrait"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".story-row")[0].QuerySelector("img")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/t/tomas.jpg");
	}

	[Test]
	public async Task APersona_NeverShowsItsAuthorsPortrait()
	{
		var cut = RenderStory([Tomas, Alice]);
		WaitForRows(cut, 3);
		var persona = cut.FindAll(".story-row")[1];
		await Assert.That(persona.QuerySelector(".story-name")!.TextContent).IsEqualTo("Mysterious Stranger");
		await Assert.That(persona.QuerySelector("img")).IsNull();
		await Assert.That(persona.QuerySelector(".story-portrait--initials")!.TextContent).IsEqualTo("MS");
	}

	[Test]
	public async Task AnOocPose_IsTheBand()
	{
		var cut = RenderStory();
		WaitForRows(cut, 3);
		await Assert.That(cut.FindAll(".story-row")[2].QuerySelector(".kit-ooc")).IsNotNull();
	}

	[Test]
	public async Task ALivePose_IsAppendedOnce_WithThePortraitOfItsObjid()
	{
		var cut = RenderStory([Tomas]);
		WaitForRows(cut, 3);
		var pose = new SceneEventMessage("42", "pose", "Tomas Reyes", "P9", "draws a bundle", "draws a bundle", [], "pose", "Lower Docks",
			1790000000000, "#312:1");
		await cut.InvokeAsync(() => _hub.RaiseScene(pose));
		await cut.InvokeAsync(() => _hub.RaiseScene(pose));
		WaitForRows(cut, 4);
		var last = cut.FindAll(".story-row")[3];
		await Assert.That(last.QuerySelector("img")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/t/tomas-room.jpg");
		await Assert.That(last.QuerySelector(".story-body")!.TextContent).IsEqualTo("draws a bundle");
	}

	[Test]
	public async Task ALivePoseUnderAPersona_KeepsInitials_EvenWithTheAuthorsObjid()
	{
		var cut = RenderStory([Tomas]);
		WaitForRows(cut, 3);
		await cut.InvokeAsync(() => _hub.RaiseScene(new SceneEventMessage("42", "pose", "The Harbourmaster", "P9", "calls out", "calls out", [],
			"pose", "Lower Docks", 1790000000000, "#312:1")));
		WaitForRows(cut, 4);
		await Assert.That(cut.FindAll(".story-row")[3].QuerySelector("img")).IsNull();
	}

	[Test]
	public async Task EventsForAnotherScene_AreIgnored()
	{
		var cut = RenderStory();
		WaitForRows(cut, 3);
		await cut.InvokeAsync(() => _hub.RaiseScene(new SceneEventMessage("99", "pose", "X", "P9", "elsewhere", "elsewhere", [], "pose", "", 1, null)));
		await Assert.That(cut.FindAll(".story-row").Count).IsEqualTo(3);
	}

	[Test]
	public async Task ARefusedJoin_IsReported()
	{
		_hub.JoinRefusal = new HubException("no character");
		bool? unavailable = null;
		var cut = RenderStory(unavailable: u => unavailable = u);
		WaitForRows(cut, 3);
		await Assert.That(unavailable).IsTrue();
	}

	[Test]
	public async Task AnotherScene_LeavesTheFirst_AndLoadsItsPoses()
	{
		var cut = RenderStory();
		WaitForRows(cut, 3);
		cut.Render(p => p.Add(x => x.SceneId, "43"));
		WaitForRows(cut, 0);
		await Assert.That(_hub.Left).IsEquivalentTo(new[] { "42" });
		await Assert.That(_hub.Joined).IsEquivalentTo(new[] { "42", "43" });
	}

	[Test]
	public async Task ALateAnswerForTheOldScene_DoesNotOverwriteTheNewOne()
	{
		// Reviewer: two room.info pushes in quick succession; scene 42's slow backlog landed after 43's.
		var gate = new TaskCompletionSource();
		_api.Hold["/api/scenes/42/poses"] = gate.Task;
		var cut = RenderStory();
		cut.Render(p => p.Add(x => x.SceneId, "43"));
		WaitForRows(cut, 0);
		gate.SetResult();
		await Task.Delay(200);
		await Assert.That(cut.FindAll(".story-row").Count).IsEqualTo(0).Because("scene 42's answer arrived after 43 was chosen");
		await Assert.That(_hub.Joined.LastOrDefault()).IsEqualTo("43");
		await Assert.That(_hub.Joined).DoesNotContain("42");
	}

	[Test]
	public async Task Disposing_LeavesTheScene()
	{
		var cut = RenderStory();
		WaitForRows(cut, 3);
		await cut.Instance.DisposeAsync();
		await Assert.That(_hub.Left).IsEquivalentTo(new[] { "42" });
	}

	[Test]
	public async Task OtherParticipants_AreMentions()
	{
		var cut = RenderStory([Tomas]);
		WaitForRows(cut, 3);
		var ilsa = cut.FindAll(".story-row")[0];
		await Assert.That(cut.FindAll(".story-row")[0].QuerySelector(".story-body a.mention")).IsNull();
		await cut.InvokeAsync(() => _hub.RaiseScene(new SceneEventMessage("42", "pose", "Ilsa Varn", "P9", "nods to Tomas", "nods to Tomas", [],
			"pose", "Lower Docks", 1790000000000, "#313:1")));
		WaitForRows(cut, 4);
		await Assert.That(cut.FindAll(".story-row")[3].QuerySelector(".story-body a.mention")!.TextContent).IsEqualTo("Tomas");
		await Assert.That(ilsa).IsNotNull();
	}

	private sealed class StoryApi : HttpMessageHandler
	{
		private const string Poses = """
		[
		  {"id":"P1","sceneId":"42","authorDbref":"#312","authorName":"Tomas Reyes","showAsName":"",
		   "originDbref":"#1201","originName":"Lower Docks","source":"pose","tags":[],"meta":{},
		   "createdAt":1790000000000,"isDeleted":false,"content":"leans on the crates","markup":"leans on the crates",
		   "editCount":1,"lastEditedAt":null,"lastEditorDbref":null,"lastEditorName":null},
		  {"id":"P2","sceneId":"42","authorDbref":"#10","authorName":"Alice","showAsName":"Mysterious Stranger",
		   "originDbref":"#1201","originName":"Lower Docks","source":"pose","tags":[],"meta":{},
		   "createdAt":1790000060000,"isDeleted":false,"content":"watches","markup":"watches",
		   "editCount":1,"lastEditedAt":null,"lastEditorDbref":null,"lastEditorName":null},
		  {"id":"P3","sceneId":"42","authorDbref":"#314","authorName":"Wren Halloway","showAsName":"",
		   "originDbref":"#1201","originName":"Lower Docks","source":"ooc","tags":["ooc"],"meta":{},
		   "createdAt":1790000120000,"isDeleted":false,"content":"brb","markup":"brb",
		   "editCount":1,"lastEditedAt":null,"lastEditorDbref":null,"lastEditorName":null}
		]
		""";

		private const string Characters = """
		[{"name":"Tomas Reyes","objid":"#312:1","created":1,"category":"","image":"/api/wiki-assets/t/tomas.jpg"},
		 {"name":"Alice","objid":"#10:1","created":1,"category":"","image":"/api/wiki-assets/a/alice.jpg"}]
		""";

		/// <summary>Paths whose answer waits for the task.</summary>
		public Dictionary<string, Task> Hold { get; } = [];

		/// <summary>How many times the backlog was read.</summary>
		public int PoseReads { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.RequestUri!.AbsolutePath.EndsWith("/poses", StringComparison.Ordinal)) PoseReads++;
			if (Hold.TryGetValue(request.RequestUri!.AbsolutePath, out var hold)) await hold;
			return Answer(request);
		}

		private static HttpResponseMessage Answer(HttpRequestMessage request)
		{
			var body = request.RequestUri!.AbsolutePath switch
			{
				"/api/scenes/42/poses" => Poses,
				"/api/scenes/43/poses" => "[]",
				"/http/characters" => Characters,
				_ => null,
			};
			return body is null
				? new HttpResponseMessage(HttpStatusCode.NotFound)
				: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
		}
	}
}
