using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>A schema page fetches its data again when softcode asks, or on its own timer while in view.</summary>
public class SchemaLiveDataTests : TrackingBunitContext
{
	private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
	}

	private sealed class Live : IAppRefreshControl
	{
		public List<string> Joined { get; } = [];
		public event Action<string>? OnAppRefresh;
		public bool IsVisible { get; set; } = true;
		public Task JoinAppAsync(string app) { Joined.Add(app); return Task.CompletedTask; }
		public Task LeaveAppAsync(string app) { Joined.Remove(app); return Task.CompletedTask; }
		public void Refresh(string app) => OnAppRefresh?.Invoke(app);
	}

	/// <summary>A clock that moves only when told, for the one timer a page starts.</summary>
	private sealed class Clock : TimeProvider
	{
		private TimerCallback? _tick;
		private object? _state;
		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			(_tick, _state) = (callback, state);
			return Substitute.For<ITimer>();
		}
		public void Tick() => _tick?.Invoke(_state);
	}

	private readonly Live _live = new();
	private readonly Clock _clock = new();
	private int _reads;

	private void Configure()
	{
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(Track(new HttpClient(new Handler(_ =>
		{
			var count = Interlocked.Increment(ref _reads);
			var data = new SchemaData(new Dictionary<string, SchemaFieldValue> { ["count"] = new(JsonSerializer.SerializeToElement($"read {count}")) });
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(data, options: SchemaJson.Options) };
		}))
		{ BaseAddress = new("https://localhost/") }));
		Services.AddMudServices().AddSingleton(factory)
			.AddSingleton(sp => new SchemaAppService(factory, NullLogger<SchemaAppService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>()
			.AddSingleton<IAppRefreshControl>(_live)
			.AddSingleton<TimeProvider>(_clock);
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static PortalSchemaDocument Sheet(string kind, int? refresh = null) => new(kind, 1, "Sheet", null,
		[new("main", null, 1, [new(null, 1, null, [new(Kind: "keyvalue", Fields: ["count"])])], null, null)],
		null, Refresh: refresh);

	private static SchemaData First => new(new Dictionary<string, SchemaFieldValue> { ["count"] = new(JsonSerializer.SerializeToElement("first")) });

	private IRenderedComponent<SchemaApplicationHost> Open(PortalSchemaDocument document, string? objid = "#5:100")
	{
		Configure();
		return Render<SchemaApplicationHost>(p => p
			.Add(x => x.Document, document).Add(x => x.Data, First)
			.Add(x => x.App, "sheet").Add(x => x.Objid, objid).Add(x => x.DataUrl, "api/built/sheet/data"));
	}

	[Test]
	public async Task A_page_joins_its_app_and_the_copy_for_its_character_and_leaves_both()
	{
		var cut = Open(Sheet("view"));
		await Assert.That(_live.Joined).IsEquivalentTo(["sheet", "sheet:#5:100"]);

		await DisposeComponentsAsync();
		await Assert.That(_live.Joined).IsEmpty();
	}

	[Test]
	public async Task A_refresh_for_its_app_or_its_character_fetches_the_data_again()
	{
		var cut = Open(Sheet("view"));

		_live.Refresh("sheet:#5:100");
		cut.WaitForState(() => cut.Markup.Contains("read 1"));

		_live.Refresh("sheet");
		cut.WaitForState(() => cut.Markup.Contains("read 2"));
	}

	[Test]
	public async Task A_refresh_for_another_app_or_character_is_ignored()
	{
		var cut = Open(Sheet("view"));

		_live.Refresh("sheet:#6:100");
		_live.Refresh("other");

		await Assert.That(_reads).IsEqualTo(0);
		await Assert.That(cut.Markup).Contains("first");
	}

	[Test]
	public async Task A_form_keeps_what_the_player_typed()
	{
		var cut = Open(Sheet("form", refresh: 30));

		_live.Refresh("sheet");
		_clock.Tick();

		await Assert.That(_reads).IsEqualTo(0);
	}

	[Test]
	public async Task A_view_with_refresh_fetches_on_its_timer_only_while_in_view()
	{
		var cut = Open(Sheet("view", refresh: 30));

		_clock.Tick();
		cut.WaitForState(() => cut.Markup.Contains("read 1"));

		_live.IsVisible = false;
		_clock.Tick();
		await Task.Delay(50);
		await Assert.That(_reads).IsEqualTo(1);
	}

	[Test]
	[Arguments(null, null)]
	[Arguments(0, null)]
	[Arguments(3, 10)]
	[Arguments(60, 60)]
	public async Task Refresh_is_off_unless_set_and_never_faster_than_ten_seconds(int? refresh, int? seconds) =>
		await Assert.That(Sheet("view", refresh).RefreshEvery).IsEqualTo(seconds is { } s ? TimeSpan.FromSeconds(s) : null);
}
