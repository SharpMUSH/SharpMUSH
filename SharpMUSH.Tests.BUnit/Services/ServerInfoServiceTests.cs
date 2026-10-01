using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Tests.Shared;
using System.Net;
using System.Text;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// <see cref="ServerInfoService"/> memoizes the server's answer for the app's lifetime. It used to
/// memoize a failure the same way, so a visitor whose first page load met a restarting server saw
/// "SharpMUSH" in place of the game's name, and the config-default guest button, until they reloaded.
/// </summary>
public class ServerInfoServiceTests
{
	private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) =>
			new(handler, disposeHandler: false) { BaseAddress = new Uri("https://localhost/") };
	}

	private static HttpResponseMessage Info(bool guests, string name) =>
		new(HttpStatusCode.OK)
		{
			Content = new StringContent(
				$$"""{"guestsEnabled":{{(guests ? "true" : "false")}},"mudName":"{{name}}"}""",
				Encoding.UTF8, "application/json")
		};

	private static HttpResponseMessage Unavailable() => new(HttpStatusCode.ServiceUnavailable);

	[Test]
	public async Task AFailedReadIsNotRemembered()
	{
		var answers = new Queue<Func<HttpResponseMessage>>([Unavailable, () => Info(false, "Elsewhere")]);
		using var handler = new CapturingHttpHandler(() => answers.Dequeue()());
		var service = new ServerInfoService(new SingleClientFactory(handler));

		await Assert.That(await service.GameNameAsync()).IsEqualTo("SharpMUSH")
			.Because("the server did not answer, so the config default stands in");
		await Assert.That(await service.GameNameAsync()).IsEqualTo("Elsewhere");
		await Assert.That(await service.GuestLoginsEnabledAsync()).IsFalse();
	}

	[Test]
	public async Task AnAnswerIsAskedForOnce()
	{
		using var handler = new CapturingHttpHandler(() => Info(false, "Elsewhere"));
		var service = new ServerInfoService(new SingleClientFactory(handler));

		await service.GameNameAsync();
		await service.GuestLoginsEnabledAsync();
		await service.GameNameAsync();

		await Assert.That(handler.Requests.Count).IsEqualTo(1);
	}

	[Test]
	public async Task ATimeoutDegradesToTheDefaults()
	{
		using var handler = new CapturingHttpHandler(() => throw new TaskCanceledException("timed out", new TimeoutException()));
		var service = new ServerInfoService(new SingleClientFactory(handler));

		await Assert.That(await service.GameNameAsync()).IsEqualTo("SharpMUSH");
		await Assert.That(await service.GuestLoginsEnabledAsync()).IsTrue();
	}

	[Test]
	public async Task ABlankNameIsTheDefaultName()
	{
		using var handler = new CapturingHttpHandler(() => Info(true, " "));
		var service = new ServerInfoService(new SingleClientFactory(handler));

		await Assert.That(await service.GameNameAsync()).IsEqualTo("SharpMUSH");
	}

	private static HttpResponseMessage WithFeatures(params string[] features) =>
		new(HttpStatusCode.OK)
		{
			Content = new StringContent(
				$$"""{"guestsEnabled":true,"mudName":"Elsewhere","features":[{{string.Join(",", features.Select(f => $"\"{f}\""))}}]}""",
				Encoding.UTF8, "application/json")
		};

	[Test]
	public async Task TheApplicationsAreTheOnesTheServerReports()
	{
		using var handler = new CapturingHttpHandler(() => WithFeatures(GameFeatures.WikiReader));
		var service = new ServerInfoService(new SingleClientFactory(handler));

		await Assert.That(await service.HasFeatureAsync(GameFeatures.WikiReader)).IsTrue();
		await Assert.That(await service.HasFeatureAsync(GameFeatures.Scenes)).IsFalse()
			.Because("a game that turned the Scene System off must not be linked to it");
	}

	[Test]
	public async Task AFailedReadAssumesANewGamesApplications()
	{
		using var handler = new CapturingHttpHandler(Unavailable);
		var service = new ServerInfoService(new SingleClientFactory(handler));

		await Assert.That(await service.HasFeatureAsync(GameFeatures.Scenes)).IsTrue();
		await Assert.That(await service.HasFeatureAsync(GameFeatures.WikiReader)).IsFalse();
	}

	[Test]
	public async Task Refresh_AsksAgain_AndTellsItsReaders()
	{
		var answers = new Queue<Func<HttpResponseMessage>>([() => WithFeatures(GameFeatures.Scenes), () => WithFeatures()]);
		using var handler = new CapturingHttpHandler(() => answers.Dequeue()());
		var service = new ServerInfoService(new SingleClientFactory(handler));
		var told = 0;
		service.Changed += () => told++;

		await Assert.That(await service.HasFeatureAsync(GameFeatures.Scenes)).IsTrue();
		service.Refresh();

		await Assert.That(told).IsEqualTo(1);
		await Assert.That(await service.HasFeatureAsync(GameFeatures.Scenes)).IsFalse();
	}
}
