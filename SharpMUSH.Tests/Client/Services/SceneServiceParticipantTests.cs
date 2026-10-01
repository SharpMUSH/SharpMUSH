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

	private (SceneService, Handler) Build(Dictionary<string, string> bodies)
	{
		var handler = new Handler(bodies);
		var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") };
		_clients.Add(http);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(http);
		return (new SceneService(factory, Substitute.For<IAccountAuthState>()), handler);
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
