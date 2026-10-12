using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Components.Applications;

namespace SharpMUSH.Tests.BUnit.Components;

public class HostedApplicationTests : TrackingBunitContext
{
	private readonly ConcurrentQueue<string> _requests = new();
	private readonly ConcurrentQueue<TrackedResponse> _responses = new();

	private void Setup(bool available = true)
	{
		var http = Track(new HttpClient(new Handler(_requests, _responses, available)) { BaseAddress = new Uri("https://api.test/") });
		var factory = Substitute.For<IHttpClientFactory>(); factory.CreateClient("api").Returns(http);
		Services.AddSingleton(factory);
		var module = JSInterop.SetupModule("./js/hosted-app.mjs");
		module.SetupModule("mount", _ => true).Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task IndependentHostsUseDistinctFramesAndBootstrapPaths()
	{
		Setup();
		var east = Render<HostedApplication>(p => p.Add(c => c.Slug, "east"));
		var west = Render<HostedApplication>(p => p.Add(c => c.Slug, "west"));
		east.WaitForAssertion(() => { if (east.FindAll("iframe").Count != 1) throw new InvalidOperationException(); });
		west.WaitForAssertion(() => { if (west.FindAll("iframe").Count != 1) throw new InvalidOperationException(); });
		await Assert.That(east.Find("iframe").Id).IsNotEqualTo(west.Find("iframe").Id);
		await Assert.That(east.Find("iframe").GetAttribute("sandbox")).IsEqualTo("allow-scripts");
		await Assert.That(_requests).Contains("/api/applications/east/host");
		await Assert.That(_requests).Contains("/api/applications/west/host");
		await Assert.That(east.Markup).DoesNotContain("rooms");
		var graph = await east.Instance.ReadTopology([10, 20]);
		await Assert.That(graph!.Rooms.Count).IsEqualTo(0);
		await Assert.That(_requests).Contains("/api/objects/topology?rooms=10&rooms=20");
		await Assert.That(_responses.Count).IsGreaterThan(0);
		await Assert.That(_responses.All(response => response.Disposed)).IsTrue()
			.Because(string.Join(", ", _responses.Where(response => !response.Disposed).Select(response => response.Path)));
	}

	[Test]
	public async Task RefusedBootstrapShowsErrorWithoutMountingPackage()
	{
		Setup(available: false);
		var host = Render<HostedApplication>(p => p.Add(c => c.Slug, "east"));
		host.WaitForAssertion(() => { if (host.FindAll("[role=alert]").Count != 1) throw new InvalidOperationException(); });
		await Assert.That(host.FindAll("iframe").Count).IsEqualTo(0);
		await Assert.That(_responses.Count).IsEqualTo(1);
		await Assert.That(_responses.All(response => response.Disposed)).IsTrue()
			.Because(string.Join(", ", _responses.Where(response => !response.Disposed).Select(response => response.Path)));
	}

	private sealed class Handler(ConcurrentQueue<string> requests, ConcurrentQueue<TrackedResponse> responses, bool available) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			requests.Enqueue(request.RequestUri!.PathAndQuery);
			// SendAsync transfers ownership to HttpClient; disposing here would return an unusable response.
			var response = new TrackedResponse(available ? HttpStatusCode.OK : HttpStatusCode.Forbidden, request.RequestUri.AbsolutePath)
			{
				Content = request.RequestUri.AbsolutePath.Contains("/assets/", StringComparison.Ordinal)
					? new StringContent("<!doctype html><title>Fixture</title>")
					: request.RequestUri.AbsolutePath.Contains("topology", StringComparison.Ordinal)
					? JsonContent.Create(new { rooms = Array.Empty<object>(), exits = Array.Empty<object>(), truncated = false })
					: JsonContent.Create(new { instanceId = request.RequestUri.AbsolutePath.Split('/')[3], configVersion = 1, config = new { rooms = new[] { 10 } }, entryUrl = "api/ui/graph-proof/assets/index.html" })
			};
			responses.Enqueue(response);
			return Task.FromResult<HttpResponseMessage>(response);
		}
	}

	private sealed class TrackedResponse(HttpStatusCode status, string path) : HttpResponseMessage(status)
	{
		public string Path { get; } = path;
		public bool Disposed { get; private set; }
		protected override void Dispose(bool disposing)
		{
			Disposed = true;
			base.Dispose(disposing);
		}
	}
}
