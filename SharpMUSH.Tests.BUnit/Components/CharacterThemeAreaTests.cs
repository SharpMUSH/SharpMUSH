using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Theme;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// The area /client paints in the acting character's own theme (Play's card and widget column), while the page
/// around it keeps one: it carries that character's tokens and parts, and follows the character in front.
/// </summary>
public class CharacterThemeAreaTests : BunitContext
{
	private readonly FakeAccountAuthState _account = new();

	public CharacterThemeAreaTests()
	{
		Services.AddMudServices();
		Services.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
		Services.AddSingleton<IAccountAuthState>(_account);
		Services.AddSingleton(sp => new ThemeService(JSInterop.JSRuntime, Substitute.For<IHttpClientFactory>(), _account,
			NullLogger<ThemeService>.Instance));
	}

	[TUnit.Core.Test]
	public async Task The_character_in_front_paints_its_own_theme_around_what_the_area_holds()
	{
		_account.ActiveCharacter = new(8, 1, "Brin", "PLAYER", IsActing: true, ThemeId: BuiltInThemes.DaylightId);
		var cut = Render<CharacterThemeArea>(p => p.AddChildContent("<p class=\"held\">Play</p>"));

		var area = cut.Find(".character-theme-area");
		await Assert.That(area.QuerySelector(".held")).IsNotNull();
		await Assert.That(area.GetAttribute("style")).Contains("color-scheme:light");
		await Assert.That(area.GetAttribute("style")).Contains($"--accent:{ThemeResolver.Resolve(BuiltInThemes.Daylight).Accent};");
		await Assert.That(area.HasAttribute("data-" + ThemeService.ReduceMotionPart)).IsTrue();

		_account.ActiveCharacter = new(9, 1, "Cole", "PLAYER", IsActing: true, ThemeId: BuiltInThemes.PhosphorId);
		_account.Fire();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Find(".character-theme-area").GetAttribute("style")!.Contains("color-scheme:dark"))
				throw new InvalidOperationException("still Brin's theme");
		});
	}

	/// <summary>
	/// While a switch connects the next character the tab already acts as it, and the page still shows the one
	/// before: an area given that page's session keeps that session's character's theme.
	/// </summary>
	[TUnit.Core.Test]
	public async Task An_area_given_a_session_keeps_that_sessions_character_while_the_tab_acts_as_another()
	{
		var brin = new AccountAuthService.CharacterSummary(8, 1, "Brin", "PLAYER", ThemeId: BuiltInThemes.DaylightId);
		var cole = new AccountAuthService.CharacterSummary(9, 1, "Cole", "PLAYER", ThemeId: BuiltInThemes.PhosphorId);
		_account.Characters = [brin, cole];
		_account.ActiveCharacter = brin;
		var terminal = Substitute.For<IPlayTerminalService>();
		terminal.Identity.Returns(new TerminalIdentity("grave", brin.Objid));
		var session = new CharacterSession(new PlayTerminalServiceHost(() => terminal), Substitute.For<ICommFeed>(), Substitute.For<IChannelWho>());
		var cut = Render<CharacterThemeArea>(p => p.Add(a => a.Session, session).AddChildContent("<p>Play</p>"));

		_account.ActiveCharacter = cole;
		_account.Fire();
		cut.Render();

		await Assert.That(cut.Find(".character-theme-area").GetAttribute("style")).Contains("color-scheme:light");
	}

	[Test]
	public async Task TheArea_ClipsWhatItPaints_SoADriftingTextureCannotScrollThePage()
	{
		// A theme whose motion drifts (Jovian) scales its texture past the area's edges; unclipped, Play's page scrolled
		// both ways. Clip, not hidden, so the area does not become a scroll container itself.
		var css = File.ReadAllText(Path.Join(ClientSource.RazorRoot, "Components", "Theme", "CharacterThemeArea.razor.css"));
		await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(css, @"\.character-theme-area\s*\{[^}]*overflow:\s*clip;")).IsTrue();
	}
}
