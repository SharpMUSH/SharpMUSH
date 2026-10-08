using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.NATS;
using SharpMUSH.Server;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// The server, not the portal, decides whether the portal may load: until the game is first ready
/// (<see cref="ServerReadiness"/>), a browser asking for the SPA shell gets the startup page, and
/// everything else answers as usual.
/// </summary>
public class PortalStartupPageTests
{
	private sealed class Harness(WebApplication app, HttpClient client, NatsConsumerRegistry consumers,
		CancellationTokenSource started, ServerReadiness readiness) : IAsyncDisposable
	{
		public HttpClient Client => client;
		public ServerReadiness Readiness => readiness;

		public void MakeReady()
		{
			started.Cancel();
			consumers.Attach(() => true);
			consumers.MarkActive("input");
			readiness.SetBridgeConnection(() => true);
		}

		public async ValueTask DisposeAsync()
		{
			client.Dispose();
			await app.DisposeAsync();
			started.Dispose();
		}
	}

	private static async Task<Harness> StartAsync()
	{
		var root = Directory.CreateTempSubdirectory("portal-startup-page-").FullName;
		var webRoot = Path.Join(root, "wwwroot");
		Directory.CreateDirectory(webRoot);
		await File.WriteAllTextAsync(Path.Join(webRoot, "index.html"), "<!DOCTYPE html><html><body>portal</body></html>");

		var consumers = new NatsConsumerRegistry();
		consumers.Registrations.Add(new(typeof(TelnetInputMessage), "input", "input", (_, _, _) => Task.CompletedTask));
		var started = new CancellationTokenSource();
		var lifetime = Substitute.For<IHostApplicationLifetime>();
		lifetime.ApplicationStarted.Returns(started.Token);
		var readiness = new ServerReadiness(consumers, lifetime);

		var builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			ContentRootPath = root,
			WebRootPath = webRoot,
			EnvironmentName = Environments.Production,
		});
		builder.WebHost.UseTestServer();
		builder.Logging.ClearProviders();
		builder.Services.AddSingleton(readiness);

		var app = builder.Build();
		app.UseRouting();
		app.UsePortalStartupPage();
		app.UsePortalStaticFiles(manifestPath: null);
		app.MapGet("/api/ping", () => "pong");
		app.MapPortal(manifestPath: null);

		await app.StartAsync();
		return new Harness(app, app.GetTestClient(), consumers, started, readiness);
	}

	[Test]
	[Arguments("/")]
	[Arguments("/wiki/some/page")]
	[Arguments("/index.html")]
	public async Task BeforeTheGameIsReady_ThePortalShell_IsTheStartupPage(string path)
	{
		await using var harness = await StartAsync();

		using var response = await harness.Client.GetAsync(path);
		var body = await response.Content.ReadAsStringAsync();

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
		await Assert.That(response.Headers.RetryAfter?.Delta).IsEqualTo(TimeSpan.FromSeconds(PortalStartupPage.RetryAfterSeconds));
		await Assert.That(body).Contains("Game is starting up");
		await Assert.That(body).DoesNotContain("portal</body>")
			.Because("the WebAssembly app must not load from a server that cannot play the game yet");
	}

	[Test]
	public async Task BeforeTheGameIsReady_TheApi_StillAnswers()
	{
		await using var harness = await StartAsync();

		using var response = await harness.Client.GetAsync("/api/ping");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task OnceReady_ThePortalShell_IsServed()
	{
		await using var harness = await StartAsync();
		harness.MakeReady();

		using var response = await harness.Client.GetAsync("/");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(await response.Content.ReadAsStringAsync()).Contains("portal</body>");
	}

	[Test]
	public async Task ABrokerBlipAfterReady_DoesNotSendBrowsersBackToTheStartupPage()
	{
		await using var harness = await StartAsync();
		harness.MakeReady();
		await Assert.That(harness.Readiness.IsReady).IsTrue();

		harness.Readiness.SetBridgeConnection(() => false);
		using var response = await harness.Client.GetAsync("/");

		await Assert.That(harness.Readiness.IsReady).IsFalse().Because("health reports the live state");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
			.Because("the bridge reconnects on its own; a player mid-session gains nothing from the startup page");
	}

	[Test]
	public async Task TheGameName_IsEncoded()
	{
		var html = PortalStartupPage.Render("<script>alert(1)</script>");

		await Assert.That(html).DoesNotContain("<script>alert(1)</script>");
		await Assert.That(html).Contains("&lt;script&gt;alert(1)&lt;/script&gt;");
	}

	[Test]
	public async Task TheGamesPictures_ReplaceTheSharpMUSHLogo()
	{
		var cosmetic = SharpMUSHOptions.Default().Cosmetic with { PortalLogo = "/api/wiki-assets/a/logo.png", PortalFavicon = "" };
		var html = PortalStartupPage.Render("Game", cosmetic);

		await Assert.That(html).Contains("<img src=\"/api/wiki-assets/a/logo.png\"");
		await Assert.That(html).Contains("<link rel=\"icon\" href=\"/api/wiki-assets/a/logo.png\"")
			.Because("an empty portal_favicon falls back to portal_logo");
		await Assert.That(PortalStartupPage.Render("Game")).Contains("<img src=\"/assets/Logo.svg\"");
	}
}

public class ServerReadinessTests
{
	private static (ServerReadiness Readiness, NatsConsumerRegistry Consumers, CancellationTokenSource Started) Create()
	{
		var consumers = new NatsConsumerRegistry();
		consumers.Registrations.Add(new(typeof(TelnetInputMessage), "input", "input", (_, _, _) => Task.CompletedTask));
		consumers.Registrations.Add(new(typeof(TelnetInputMessage), "other", "other", (_, _, _) => Task.CompletedTask));
		var started = new CancellationTokenSource();
		var lifetime = Substitute.For<IHostApplicationLifetime>();
		lifetime.ApplicationStarted.Returns(started.Token);
		return (new ServerReadiness(consumers, lifetime), consumers, started);
	}

	[Test]
	public async Task NotReady_UntilTheHostTheConsumersAndTheBridgeAreAllUp()
	{
		var (readiness, consumers, started) = Create();
		using var _ = started;

		await Assert.That(readiness.Pending()).IsEquivalentTo(["host", "input-consumers", "output-bridge"]);

		started.Cancel();
		consumers.Attach(() => true);
		consumers.MarkActive("input");
		readiness.SetBridgeConnection(() => true);
		await Assert.That(readiness.IsReady).IsFalse().Because("one consumer of two is not yet consuming");
		await Assert.That(readiness.Pending()).IsEquivalentTo(["input-consumers"]);

		consumers.MarkActive("other");
		await Assert.That(readiness.IsReady).IsTrue();
		await Assert.That(readiness.Pending()).IsEmpty();
	}

	[Test]
	public async Task AConsumerGroupRestart_IsNotReady_ButHasBeenReadyHolds()
	{
		var (readiness, consumers, started) = Create();
		using var _ = started;
		started.Cancel();
		consumers.Attach(() => true);
		consumers.MarkActive("input");
		consumers.MarkActive("other");
		readiness.SetBridgeConnection(() => true);
		await Assert.That(readiness.IsReady).IsTrue();

		consumers.MarkAllInactive();

		await Assert.That(consumers.AllActive).IsFalse();
		await Assert.That(consumers.WaitUntilReadyAsync(CancellationToken.None).IsCompleted).IsTrue()
			.Because("the first-ready signal the readiness publish waits on is one-shot");
		await Assert.That(readiness.IsReady).IsFalse();
		await Assert.That(readiness.HasBeenReady).IsTrue();
	}

	[Test]
	public async Task ABrokerOutage_IsNotReady_EvenThoughNothingFailed()
	{
		// The NATS client reconnects on its own: during an outage the consumers and the bridge keep
		// their subscriptions and see no error, so only the connection's own state shows it.
		var (readiness, consumers, started) = Create();
		using var _ = started;
		var consumerConnectionOpen = true;
		var bridgeOpen = true;
		started.Cancel();
		consumers.Attach(() => consumerConnectionOpen);
		consumers.MarkActive("input");
		consumers.MarkActive("other");
		readiness.SetBridgeConnection(() => bridgeOpen);
		await Assert.That(readiness.IsReady).IsTrue();

		consumerConnectionOpen = false;
		bridgeOpen = false;

		await Assert.That(readiness.IsReady).IsFalse();
		await Assert.That(readiness.Pending()).IsEquivalentTo(["input-consumers", "output-bridge"]);

		consumerConnectionOpen = true;
		bridgeOpen = true;
		await Assert.That(readiness.IsReady).IsTrue().Because("reconnecting restores it with no call from anyone");
	}
}
