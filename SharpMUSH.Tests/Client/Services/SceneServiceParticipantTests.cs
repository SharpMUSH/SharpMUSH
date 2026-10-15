using System.Net;
using System.Text;
using NSubstitute;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>The profile's Recent scenes and Often plays with read <c>?participant=</c> and <c>/partners</c>.</summary>
public class SceneServiceParticipantTests : IDisposable
{
	private readonly List<HttpClient> _clients = [];

	public void Dispose()
	{
		foreach (var client in _clients)
		{
			client.Dispose();
		}
	}

	private sealed class Handler(Dictionary<string, string> bodies) : HttpMessageHandler
	{
		public List<string> Paths { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.PathAndQuery;
			Paths.Add(path);
			return Task.FromResult(bodies.TryGetValue(path, out var body)
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
				: new HttpResponseMessage(HttpStatusCode.InternalServerError));
		}
	}

	private (SceneService, Handler) Build(Dictionary<string, string> bodies, bool askFeatures = false)
	{
		var handler = new Handler(bodies);
		var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") };
		_clients.Add(http);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(http);
		return (new SceneService(factory, Substitute.For<IAccountAuthState>(), askFeatures ? new ServerInfoService(factory) : null),
			handler);
	}

	/// <summary>
	/// The scene API is the Scene plugin's. A game without the Scene System, or a server the portal cannot ask, is not
	/// asked for scenes at all: the read is a failure the caller already handles, and no request goes out.
	/// </summary>
	[Test]
	[Arguments("""{"guestsEnabled":true,"mudName":"Test","features":[]}""")]
	[Arguments(null)]
	public async Task WithoutTheSceneSystem_NothingIsAsked(string? serverInfo)
	{
		var bodies = new Dictionary<string, string>
		{
			["/api/scenes/partners?participant=%23312&count=6"] = """[{"dbref":"#313","name":"Ilsa Varn","scenes":4}]""",
		};
		if (serverInfo is not null) bodies["/api/server-info"] = serverInfo;
		var (service, handler) = Build(bodies, askFeatures: true);

		await Assert.That((await service.GetPartnersAsync("#312", 6)).Expect<ApiFailure>().Kind).IsEqualTo(ApiFailureKind.NotFound);
		await Assert.That((await service.GetActiveScenesAsync()).Expect<ApiFailure>()).IsNotNull();
		await Assert.That((await service.StreamPosesAsync("42", _ => Task.CompletedTask)).Expect<ApiFailure>()).IsNotNull();
		await Assert.That(handler.Paths.Where(p => p.StartsWith("/api/scenes"))).IsEmpty();
	}

	[Test]
	public async Task WithTheSceneSystem_ScenesAreAsked()
	{
		var (service, handler) = Build(new()
		{
			["/api/server-info"] = """{"guestsEnabled":true,"mudName":"Test","features":["scenes"]}""",
			["/api/scenes/partners?participant=%23312&count=6"] = """[{"dbref":"#313","name":"Ilsa Varn","scenes":4}]""",
		}, askFeatures: true);

		await Assert.That((await service.GetPartnersAsync("#312", 6)).Expect<IReadOnlyList<ScenePartner>>()).Count().IsEqualTo(1);
		await Assert.That(handler.Paths).Contains("/api/scenes/partners?participant=%23312&count=6");
	}

	[Test]
	public async Task ParticipantScenes_AskByDbref_AndMapToSummaries()
	{
		var (service, handler) = Build(new()
		{
			["/api/scenes?participant=%23312&count=3"] = """
				[{"id":"42","status":"active","isPublic":true,"isTempRoom":false,"startedAt":1,"lastActivityAt":2,"poseCount":12,
				  "ownerName":"Ilsa","starterName":"Ilsa","roomName":"Lower Docks","meta":{"title":"Salt Market at Dusk"}}]
				""",
		});

		var scenes = (await service.GetParticipantScenesAsync("#312", 3)).Expect<IReadOnlyList<SceneSummary>>();
		await Assert.That(scenes[0].Id).IsEqualTo("42");
		await Assert.That(scenes[0].IsLive).IsTrue();
		await Assert.That(handler.Paths).Contains("/api/scenes?participant=%23312&count=3");
	}

	[Test]
	public async Task Partners_MapNameDbrefAndCount_AndAFailureIsAFailure()
	{
		var (service, _) = Build(new()
		{
			["/api/scenes/partners?participant=%23312&count=6"] = """[{"dbref":"#313","name":"Ilsa Varn","scenes":4}]""",
		});

		var partners = (await service.GetPartnersAsync("#312", 6)).Expect<IReadOnlyList<ScenePartner>>();
		await Assert.That(partners.Single()).IsEqualTo(new ScenePartner("#313", "Ilsa Varn", 4));

		await Assert.That((await service.GetPartnersAsync("#999", 6)).Expect<ApiFailure>()).IsNotNull();
	}
}
