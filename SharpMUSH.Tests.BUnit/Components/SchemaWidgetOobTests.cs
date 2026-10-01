using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>Serves the weather schema only; an OOB-backed widget must never ask for data over HTTP.</summary>
internal sealed class WeatherSchemaHandler : HttpMessageHandler
{
	private const string Schema = """
	{"kind":"view","schema_version":1,"pages":[{"key":"weather","order":1,"sections":[
	  {"name":"Weather","order":1,"elements":[
	    {"kind":"field","key":"sky","label":"Sky","type":"text","visible_to":"public"}]}]}]}
	""";

	public List<string> Requested { get; } = [];

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		Requested.Add(request.RequestUri!.AbsolutePath);
		return Task.FromResult(request.RequestUri!.AbsolutePath == "/http/weather/schema"
			? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Schema, Encoding.UTF8, "application/json") }
			: new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

/// <summary>An OOB store that counts its <see cref="ChannelUpdated"/> subscribers, over a real store.</summary>
internal sealed class CountingOobStore : IOobChannelStore
{
	private readonly OobChannelStore _inner = new();

	public int Subscribers { get; private set; }

	public event Action<string>? ChannelUpdated
	{
		add
		{
			_inner.ChannelUpdated += value;
			Subscribers++;
		}
		remove
		{
			_inner.ChannelUpdated -= value;
			Subscribers--;
		}
	}

	public event Action? RoomChanged
	{
		add => _inner.RoomChanged += value;
		remove => _inner.RoomChanged -= value;
	}

	public void Set(string package, string dataJson) => _inner.Set(package, dataJson);
	public string? Get(string package) => _inner.Get(package);
	public IReadOnlyCollection<string> Packages => _inner.Packages;
	public RoomState Room => _inner.Room;
	public void Clear() => _inner.Clear();
}

/// <summary>
/// An application with an <c>OobPackage</c> (README §7.4) renders the latest payload of that package from
/// the play connection's OOB store as its schema data, and follows each new push for it.
/// </summary>
public class SchemaWidgetOobTests : TrackingBunitContext
{
	private readonly CountingOobStore _oob = new();
	private readonly WeatherSchemaHandler _http = new();

	public SchemaWidgetOobTests()
	{
		var apiClient = Track(new HttpClient(_http) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		// A DataUrl as well, to prove the OOB package wins over it rather than being fetched beside it.
		var weather = new PortalApplication(
			"weather", "Weather", null, "Widget", "http/weather/schema", "http/weather/data", null, "Player", null,
			["RightSidebar"], 30, Scope: "play", OobPackage: "weather.now");

		var play = Substitute.For<IPlayTerminalService>();
		play.OobChannels.Returns(_oob);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(new ApplicationCatalog([weather]))
			.AddSingleton(play)
			.AddSingleton(sp => new ApplicationRegistryClient(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<ApplicationRegistryClient>.Instance))
			.AddSingleton(sp => new SchemaAppService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<SchemaAppService>.Instance))
			.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static string Payload(string sky) => """{"fields":{"sky":{"value":""" + JsonSerializer.Serialize(sky) + ""","visible":true}}}""";

	/// <summary>Renders the widget and waits until it has settled: fields shown, the empty state, or unavailable.</summary>
	private IRenderedComponent<SchemaWidget> RenderWeather()
	{
		var cut = Render<SchemaWidget>(p => p.Add(w => w.WidgetName, "weather"));
		cut.WaitForAssertion(() =>
		{
			if (!new[] { "Sky", "WidNothingToDisplay", "WidSchemaUnavailable" }.Any(cut.Markup.Contains))
				throw new InvalidOperationException("widget still loading");
		}, TimeSpan.FromSeconds(5));
		return cut;
	}

	private static IRenderedComponent<SchemaWidget> WaitFor(IRenderedComponent<SchemaWidget> cut, string text)
	{
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains(text))
				throw new InvalidOperationException($"'{text}' not rendered yet");
		}, TimeSpan.FromSeconds(5));
		return cut;
	}

	[TUnit.Core.Test]
	public async Task RendersTheLatestPayload_AndNeverFetchesDataOverHttp()
	{
		_oob.Set("weather.now", Payload("Overcast"));

		WaitFor(RenderWeather(), "Overcast");

		await Assert.That(_http.Requested).DoesNotContain("/http/weather/data");
	}

	[TUnit.Core.Test]
	public async Task ReRendersWhenThatPackageIsPushed()
	{
		_oob.Set("weather.now", Payload("Overcast"));
		var cut = WaitFor(RenderWeather(), "Overcast");

		await cut.InvokeAsync(() => _oob.Set("weather.now", Payload("Thunderstorm")));

		WaitFor(cut, "Thunderstorm");
		await Assert.That(cut.Markup).DoesNotContain("Overcast");
	}

	[TUnit.Core.Test]
	public async Task IgnoresOtherPackages()
	{
		_oob.Set("weather.now", Payload("Overcast"));
		var cut = WaitFor(RenderWeather(), "Overcast");

		await cut.InvokeAsync(() => _oob.Set("room.weather", Payload("Thunderstorm")));

		await Assert.That(cut.Markup).Contains("Overcast");
		await Assert.That(cut.Markup).DoesNotContain("Thunderstorm");
	}

	[TUnit.Core.Test]
	[Arguments("{not json")]
	[Arguments("[1,2,3]")]
	[Arguments("\"a string\"")]
	[Arguments("{\"fields\":[1,2]}")]
	[Arguments("")]
	public async Task MalformedPayload_RendersTheEmptyState(string payload)
	{
		_oob.Set("weather.now", payload);

		var cut = RenderWeather();

		// The schema loaded (not "unavailable"), and with no data the view renderer shows its empty state.
		await Assert.That(cut.Markup).Contains("WidNothingToDisplay");
		await Assert.That(cut.Markup).DoesNotContain("WidSchemaUnavailable");
	}

	[TUnit.Core.Test]
	public async Task AbsentPayload_RendersTheEmptyState_ThenFillsOnTheFirstPush()
	{
		var cut = RenderWeather();
		await Assert.That(cut.Markup).Contains("WidNothingToDisplay");

		await cut.InvokeAsync(() => _oob.Set("weather.now", Payload("Clear")));

		WaitFor(cut, "Clear");
		await Assert.That(cut.Markup).Contains("Sky");
	}

	[TUnit.Core.Test]
	public async Task Dispose_Unsubscribes()
	{
		RenderWeather();
		await Assert.That(_oob.Subscribers).IsEqualTo(1);

		await DisposeComponentsAsync();

		await Assert.That(_oob.Subscribers).IsEqualTo(0);
	}
}
