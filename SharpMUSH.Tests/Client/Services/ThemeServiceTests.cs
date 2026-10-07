using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// The portal's theme service: it starts from the theme this browser last used, then paints the acting character's
/// own theme and accent once the game's themes arrive, and follows character switches and saved accents.
/// </summary>
public class ThemeServiceTests : TrackingTestContext
{
	private static readonly PortalTheme House = BuiltInThemes.Phosphor with
	{
		Id = "house",
		Name = "House",
		BuiltIn = false,
		Tokens = new Dictionary<string, string>(BuiltInThemes.Phosphor.Tokens) { ["accent"] = "#ff5c7a" },
	};

	/// <summary>Answers with the themes, or, when they are held back, never answers.</summary>
	private sealed class ThemesHandler(PortalThemesResponse themes, bool held) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> held
				? new TaskCompletionSource<HttpResponseMessage>().Task
				: Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(themes) });
	}

	private readonly FakeAccountAuthState _account = new();
	private readonly Dictionary<string, string?> _storage = [];

	private ThemeService Build(string defaultThemeId = "house", bool themesHeld = false)
	{
		var http = Track(new HttpClient(new ThemesHandler(new PortalThemesResponse([.. BuiltInThemes.All, House], defaultThemeId), themesHeld))
		{
			BaseAddress = new Uri("http://localhost"),
		});
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);

		var js = Substitute.For<IJSRuntime>();
		js.InvokeAsync<string?>(Arg.Any<string>(), Arg.Any<object?[]?>())
			.Returns(call => new ValueTask<string?>(_storage.GetValueOrDefault((string)call.ArgAt<object?[]>(1)![0]!)));
		js.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(Arg.Any<string>(), Arg.Any<object?[]?>())
			.Returns(call =>
			{
				var args = call.ArgAt<object?[]>(1)!;
				_storage[(string)args[0]!] = (string?)args[1];
				return new ValueTask<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(default(Microsoft.JSInterop.Infrastructure.IJSVoidResult)!);
			});

		return new ThemeService(js, factory, _account, Substitute.For<ILogger<ThemeService>>());
	}

	private static AccountAuthService.CharacterSummary Character(string? themeId = null, string? accent = null) =>
		new(7, 1, "Seven", "PLAYER", IsActing: true, themeId, accent);

	[Test]
	public async Task BeforeTheThemesArriveThePortalIsPhosphor()
	{
		var service = Build();

		await Assert.That(service.Current.ThemeId).IsEqualTo(BuiltInThemes.PhosphorId);
	}

	[Test]
	public async Task ACharacterThatChoseNothingGetsTheGamesDefault()
	{
		_account.ActiveCharacter = Character();
		var service = Build();

		await service.ReloadAsync();

		await Assert.That(service.Current.ThemeId).IsEqualTo("house");
		await Assert.That(service.Current.Accent).IsEqualTo("#ff5c7a");
		await Assert.That(service.DefaultThemeId).IsEqualTo("house");
	}

	[Test]
	public async Task TheActingCharactersThemeAndAccentArePainted()
	{
		_account.ActiveCharacter = Character("daylight", "#ffb454");
		var service = Build();

		await service.ReloadAsync();

		await Assert.That(service.Current.ThemeId).IsEqualTo("daylight");
		await Assert.That(service.Current.Dark).IsFalse();
		await Assert.That(service.Current.RequestedAccent).IsEqualTo("#ffb454");
		await Assert.That(service.Current.AccentAdjusted).IsTrue();
	}

	[Test]
	public async Task SwitchingCharactersOrSavingAnAccentRepaints()
	{
		_account.ActiveCharacter = Character();
		var service = Build();
		await service.ReloadAsync();
		var changes = 0;
		service.OnThemeChanged += () => changes++;

		_account.ActiveCharacter = Character(accent: "#5aa9ff");
		_account.FireAppearance();
		await Assert.That(service.Current.Accent).IsEqualTo("#5aa9ff");

		_account.ActiveCharacter = Character("daylight");
		_account.Fire();
		await Assert.That(service.Current.ThemeId).IsEqualTo("daylight");

		_account.Fire();
		await Assert.That(changes).IsEqualTo(2).Because("a change that paints nothing new raises nothing");
	}

	[Test]
	public async Task TheAppliedThemeIsKeptForTheNextVisit()
	{
		_account.ActiveCharacter = Character("daylight");
		var first = Build();
		await first.ReloadAsync();
		await Assert.That(_storage.ContainsKey(ThemeService.CacheKey)).IsTrue();

		var next = Build(themesHeld: true);
		await next.InitializeAsync();

		await Assert.That(next.Current.ThemeId).IsEqualTo("daylight");
		await Assert.That(JsonDocument.Parse(_storage[ThemeService.CacheKey]!).RootElement.GetProperty("Css").GetString())
			.StartsWith(":root{color-scheme:light;");
	}

	[Test]
	public async Task APreviewPaintsOverTheCharactersThemeUntilCleared()
	{
		var service = Build();
		await service.ReloadAsync();
		var preview = ThemeResolver.Resolve(BuiltInThemes.Daylight);

		service.Preview(preview);
		await Assert.That(service.Current).IsSameReferenceAs(preview);

		service.Preview(null);
		await Assert.That(service.Current.ThemeId).IsEqualTo("house");
	}
}
