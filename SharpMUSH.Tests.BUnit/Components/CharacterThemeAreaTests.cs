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
}
