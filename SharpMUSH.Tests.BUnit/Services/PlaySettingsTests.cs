using SharpMUSH.Client.Models;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// Play's settings that follow the player: read from and written to the account's <c>portal.play</c> group, kept in
/// the browser without an account, and moved off the keys Play used to keep in the browser.
/// </summary>
public class PlaySettingsTests : TrackingBunitContext
{
	/// <summary>The account's <c>portal.play</c> values as a fake server keeps them.</summary>
	private sealed class AccountGroup : HttpMessageHandler
	{
		public readonly Dictionary<string, JsonElement> Stored = new(StringComparer.OrdinalIgnoreCase);
		public readonly List<Dictionary<string, JsonElement>> Puts = [];

		/// <summary>When set, a read's answer is made, then held until this finishes.</summary>
		public Task? HoldReads;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.RequestUri!.AbsolutePath != "/api/preferences/portal.play")
				return new HttpResponseMessage(HttpStatusCode.NotFound);
			if (request.Method == HttpMethod.Put)
			{
				var body = (await request.Content!.ReadFromJsonAsync<PreferenceUpdateRequest>(cancellationToken))!;
				Puts.Add(new Dictionary<string, JsonElement>(body.Values));
				foreach (var (key, value) in body.Values) Stored[key] = value;
			}

			var group = Group();
			if (request.Method == HttpMethod.Get && HoldReads is { } hold) await hold;
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(group) };
		}

		private PreferenceGroupResponse Group()
		{
			var items = PlayPreferences.Group.Keys.Select(spec =>
			{
				var fallback = PreferenceJson.Typed(spec, spec.Default ?? "");
				var set = Stored.TryGetValue(spec.Key, out var value);
				return new PreferenceResponse(spec.Key, Preferences.TypeName(spec.Type), spec.Label, spec.Description,
					set ? value : fallback, fallback, set, [], null, null, null);
			}).ToList();
			return new PreferenceGroupResponse("portal.play", "portal", "account", "Play", "",
				items.ToDictionary(i => i.Key, i => i.Value!.Value), items, null);
		}
	}

	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow() => Now;
	}

	private readonly AccountGroup _server = new();
	private readonly Clock _clock = new();

	public PlaySettingsTests() => JSInterop.Mode = JSRuntimeMode.Loose;

	private async Task<PlaySettings> Settings(bool signedIn)
	{
		var client = Track(new HttpClient(_server) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult(signedIn ? "token-1" : null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("headwiz");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.mustChangePassword").SetResult(bool.FalseString);
		var auth = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []);
		await auth.InitAsync();
		var settings = new PlaySettings(new PreferencesService(factory), auth, JSInterop.JSRuntime, _clock);
		await settings.LoadAsync();
		return settings;
	}

	[Test]
	public async Task ChannelGroupsAreKeptOnTheAccount_AsAJsonArray()
	{
		var settings = await Settings(signedIn: true);
		using var store = new PlayCommGroupStore(settings);
		var groups = new CommGroups(store);

		await Assert.That(await groups.SaveAsync(null, "Roleplay", ["Public", "Scenes"]) is CommGroup).IsTrue();

		var put = _server.Puts.Single()[PlayPreferences.CommGroups];
		await Assert.That(put.ValueKind).IsEqualTo(JsonValueKind.Array);
		await Assert.That(put[0].GetProperty("name").GetString()).IsEqualTo("Roleplay");

		using var again = new PlayCommGroupStore(await Settings(signedIn: true));
		var read = CommGroup.FromJson(await again.ReadAsync());
		await Assert.That(read.Single().Channels).IsEquivalentTo(new[] { "Public", "Scenes" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task ChannelGroupsChangedElsewhere_AreNews_ButTheStoresOwnWriteIsNot()
	{
		var settings = await Settings(signedIn: true);
		using var store = new PlayCommGroupStore(settings);
		var news = 0;
		store.Changed += () => news++;

		await store.WriteAsync("""[{"name":"Roleplay","channels":["Public","Scenes"]}]""");
		await Assert.That(news).IsEqualTo(0);

		_server.Stored[PlayPreferences.CommGroups] = JsonSerializer.SerializeToElement(new[] { new { name = "Staff", channels = new[] { "Admin", "Jobs" } } });
		_clock.Now = _clock.Now.AddMinutes(5);
		await settings.OnShownAsync();
		await Assert.That(news).IsEqualTo(1);
		await Assert.That(CommGroup.FromJson(await store.ReadAsync()).Single().Name).IsEqualTo("Staff");
	}

	[Test]
	public async Task TheAccountsValuesAreRead()
	{
		_server.Stored[PlayPreferences.HideChannels] = JsonSerializer.SerializeToElement(true);

		var settings = await Settings(signedIn: true);

		await Assert.That(settings.OnAccount).IsTrue();
		await Assert.That(settings.Get(PlayPreferences.HideChannels, false)).IsTrue();
		await Assert.That(settings.Get(PlayPreferences.Echo, false)).IsTrue().Because("echo's default is on");
	}

	[Test]
	public async Task ASettingIsWrittenToTheAccount()
	{
		var settings = await Settings(signedIn: true);
		var changed = new List<string>();
		settings.Changed += changed.Add;

		await Assert.That(await settings.SetAsync(PlayPreferences.Echo, false)).IsTrue();

		await Assert.That(_server.Puts.Single()[PlayPreferences.Echo].GetBoolean()).IsFalse();
		await Assert.That(settings.Get(PlayPreferences.Echo, true)).IsFalse();
		await Assert.That(changed).Contains(PlayPreferences.Echo);
	}

	[Test]
	public async Task WhatThisBrowserKeptBeforeMovesToAnAccountThatHasNone()
	{
		JSInterop.Setup<string?>("localStorage.getItem", "play.hidepages").SetResult("on");
		JSInterop.Setup<string?>("localStorage.getItem", "play.echo").SetResult("off");
		_server.Stored[PlayPreferences.Echo] = JsonSerializer.SerializeToElement(true);

		var settings = await Settings(signedIn: true);

		await Assert.That(settings.Get(PlayPreferences.HidePages, false)).IsTrue();
		await Assert.That(settings.Get(PlayPreferences.Echo, false)).IsTrue()
			.Because("the account already had echo set, and it wins over this browser's old value");
		await Assert.That(_server.Puts.Single().Keys).IsEquivalentTo(new[] { PlayPreferences.HidePages });
		var removed = JSInterop.Invocations.Where(i => i.Identifier == "localStorage.removeItem").Select(i => (string?)i.Arguments[0]);
		await Assert.That(removed).Contains("play.hidepages");
	}

	[Test]
	public async Task WithoutAnAccountTheBrowserKeepsThem()
	{
		JSInterop.Setup<string?>("localStorage.getItem", "play.echo").SetResult("off");

		var settings = await Settings(signedIn: false);

		await Assert.That(settings.OnAccount).IsFalse();
		await Assert.That(settings.Get(PlayPreferences.Echo, true)).IsFalse();
		await settings.SetAsync(PlayPreferences.HidePages, true);
		await Assert.That(_server.Puts).IsEmpty();
		var written = JSInterop.Invocations.Where(i => i.Identifier == "localStorage.setItem")
			.Select(i => ((string?)i.Arguments[0], (string?)i.Arguments[1])).ToList();
		await Assert.That(written).Contains(("play.pref.echo", "false"));
		await Assert.That(written).Contains(("play.pref.hide_pages", "true"));
	}

	[Test]
	public async Task AChangeAnotherComputerMadeShowsWhenTheTabComesBack()
	{
		var settings = await Settings(signedIn: true);
		var changed = new List<string>();
		settings.Changed += changed.Add;
		_server.Stored[PlayPreferences.HidePages] = JsonSerializer.SerializeToElement(true);

		await settings.OnShownAsync();
		await Assert.That(settings.Get(PlayPreferences.HidePages, false)).IsFalse().Because("it was read a moment ago");

		_clock.Now += TimeSpan.FromSeconds(6);
		await settings.OnShownAsync();

		await Assert.That(settings.Get(PlayPreferences.HidePages, false)).IsTrue();
		await Assert.That(changed).IsEquivalentTo(new[] { PlayPreferences.HidePages });
	}

	[Test]
	public async Task WhatThisBrowserKeptWhileSignedOutMovesToTheAccount()
	{
		JSInterop.Setup<string?>("localStorage.getItem", "play.pref.hide_channels").SetResult("true");

		var settings = await Settings(signedIn: true);

		await Assert.That(_server.Puts.Single()[PlayPreferences.HideChannels].GetBoolean()).IsTrue();
		await Assert.That(settings.Get(PlayPreferences.HideChannels, false)).IsTrue();
		var removed = JSInterop.Invocations.Where(i => i.Identifier == "localStorage.removeItem").Select(i => (string?)i.Arguments[0]);
		await Assert.That(removed).Contains("play.pref.hide_channels");
	}

	[Test]
	public async Task AReadUnderWayDoesNotUndoAChangeMadeMeanwhile()
	{
		var settings = await Settings(signedIn: true);
		_server.Stored[PlayPreferences.HidePages] = JsonSerializer.SerializeToElement(true);
		var hold = new TaskCompletionSource();
		_server.HoldReads = hold.Task;
		_clock.Now += TimeSpan.FromSeconds(6);

		var reading = settings.OnShownAsync();
		await settings.SetAsync(PlayPreferences.HidePages, false);
		hold.SetResult();
		await reading;

		await Assert.That(settings.Get(PlayPreferences.HidePages, true)).IsFalse()
			.Because("the read's answer was made before the change");
	}
}
