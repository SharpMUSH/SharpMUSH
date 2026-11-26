using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Models;
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

		/// <summary>When set, the next write is stored and its answer made, then held until this finishes.</summary>
		public Task? HoldNextWrite;

		/// <summary>How many reads have reached the server.</summary>
		public int Reads;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.Method == HttpMethod.Get) Reads++;
			const string group = "/api/preferences/portal.play";
			var path = request.RequestUri!.AbsolutePath;
			if (request.Method == HttpMethod.Delete && path.StartsWith(group + "/", StringComparison.Ordinal))
			{
				Stored.Remove(path[(group.Length + 1)..]);
				path = group;
			}

			if (path != group)
				return new HttpResponseMessage(HttpStatusCode.NotFound);
			if (request.Method == HttpMethod.Put)
			{
				var body = (await request.Content!.ReadFromJsonAsync<PreferenceUpdateRequest>(cancellationToken))!;
				Puts.Add(new Dictionary<string, JsonElement>(body.Values));
				foreach (var (key, value) in body.Values) Stored[key] = value;
			}

			var answer = Group();
			if (request.Method == HttpMethod.Get && HoldReads is { } hold) await hold;
			if (request.Method == HttpMethod.Put && HoldNextWrite is { } held)
			{
				HoldNextWrite = null;
				await held;
			}
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(answer) };
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
		var settings = await Unread(signedIn);
		await settings.LoadAsync();
		return settings;
	}

	/// <summary>The settings, not read yet.</summary>
	private async Task<PlaySettings> Unread(bool signedIn)
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
		return new PlaySettings(new PreferencesService(factory), auth, JSInterop.JSRuntime, _clock);
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
		var writing = settings.SetAsync(PlayPreferences.HidePages, false);
		await Assert.That(settings.Get(PlayPreferences.HidePages, true)).IsFalse().Because("a change shows at once");
		hold.SetResult();
		await reading;

		await Assert.That(settings.Get(PlayPreferences.HidePages, true)).IsFalse()
			.Because("the read's answer was made before the change");
		await Assert.That(await writing).IsTrue();
		await Assert.That(_server.Stored[PlayPreferences.HidePages].GetBoolean()).IsFalse();
	}

	[Test]
	public async Task AnEarlierWritesAnswerDoesNotUndoALaterChange()
	{
		var settings = await Settings(signedIn: true);
		var held = new TaskCompletionSource();
		_server.HoldNextWrite = held.Task;

		var first = settings.SetAsync(PlayPreferences.HidePages, true);
		var second = settings.SetAsync(PlayPreferences.HideChannels, true);
		held.SetResult();
		await first;
		await second;

		await Assert.That(settings.Get(PlayPreferences.HidePages, false)).IsTrue();
		await Assert.That(settings.Get(PlayPreferences.HideChannels, false)).IsTrue()
			.Because("the first write's answer, made before the second change, does not put the old value back");
	}

	[Test]
	public async Task AChangeMadeWhileTheBrowsersValuesMoveIsNotOverwritten()
	{
		JSInterop.Setup<string?>("localStorage.getItem", "play.echo").SetResult("off");
		var hold = new TaskCompletionSource();
		_server.HoldReads = hold.Task;
		var settings = await Unread(signedIn: true);

		var reading = settings.LoadAsync();
		var writing = settings.SetAsync(PlayPreferences.Echo, true);
		hold.SetResult();
		await reading;
		await writing;

		await Assert.That(_server.Puts.SelectMany(put => put).Where(pair => pair.Key == PlayPreferences.Echo)
				.Select(pair => pair.Value.GetBoolean()))
			.IsEquivalentTo(new[] { true })
			.Because("the browser's old value is not moved over the change the player made meanwhile");
		await Assert.That(settings.Get(PlayPreferences.Echo, false)).IsTrue();
	}

	[Test]
	public async Task AResetOnTheAccountShowsTheDefault()
	{
		_server.Stored[PlayPreferences.Echo] = JsonSerializer.SerializeToElement(false);
		var settings = await Settings(signedIn: true);

		await Assert.That(await settings.ResetAsync(PlayPreferences.Echo)).IsTrue();

		await Assert.That(settings.Get(PlayPreferences.Echo, false)).IsTrue().Because("echo's default is on");
	}

	[Test]
	public async Task AReadAskedForDuringAWriteWaitsForIt()
	{
		var settings = await Settings(signedIn: true);
		var held = new TaskCompletionSource();
		_server.HoldNextWrite = held.Task;
		_clock.Now += TimeSpan.FromSeconds(6);

		var reads = _server.Reads;
		var writing = settings.SetAsync(PlayPreferences.HidePages, true);
		var reading = settings.OnShownAsync();
		await Task.Delay(50);
		await Assert.That(_server.Reads).IsEqualTo(reads).Because("the read waits for the write to finish");
		held.SetResult();
		await writing;
		await reading;

		await Assert.That(settings.Get(PlayPreferences.HidePages, false)).IsTrue();
		await Assert.That(_server.Reads).IsEqualTo(reads + 1);
	}

	[Test]
	public async Task WithoutAnAccountAValueInTheCurrentFormIsNotWrittenAgain()
	{
		JSInterop.Setup<string?>("localStorage.getItem", "play.pref.echo").SetResult("true");
		JSInterop.Setup<string?>("localStorage.getItem", "play.echo").SetResult("off");

		var settings = await Settings(signedIn: false);

		await Assert.That(settings.Get(PlayPreferences.Echo, false)).IsTrue().Because("the current form wins");
		var touched = JSInterop.Invocations
			.Where(i => i.Identifier is "localStorage.setItem" or "localStorage.removeItem")
			.Select(i => (i.Identifier, (string?)i.Arguments[0])).ToList();
		await Assert.That(touched).IsEquivalentTo(new[] { ("localStorage.removeItem", (string?)"play.echo") })
			.Because("only the old key is forgotten; another tab may have changed the current one");
	}

	[Test]
	public async Task HighlightRulesAreSavedToTheAccount()
	{
		var settings = await Settings(signedIn: true);
		using var rules = new HighlightRules(settings);

		await Assert.That(await rules.SaveAsync([new HighlightRule("a", "Ann")])).IsEqualTo(HighlightSave.Saved);

		var stored = _server.Puts.Single()[HighlightRules.Key];
		await Assert.That(stored[0].GetProperty("pattern").GetString()).IsEqualTo("Ann");
		await Assert.That(rules.Rules.Single().Id).IsEqualTo("a");
	}

	[Test]
	public async Task HighlightRulesTooLongToKeepAreNotSent()
	{
		var settings = await Settings(signedIn: true);
		using var rules = new HighlightRules(settings);
		var many = Enumerable.Range(0, HighlightRule.MaxRules)
			.Select(n => new HighlightRule($"r{n}", new string('x', 1000), Rewrite: new string('y', HighlightRule.MaxRewriteLength)))
			.ToArray();

		await Assert.That(await rules.SaveAsync(many)).IsEqualTo(HighlightSave.TooLarge);
		await Assert.That(_server.Puts).IsEmpty();
		await Assert.That(rules.Rules).IsEmpty();
	}
}
