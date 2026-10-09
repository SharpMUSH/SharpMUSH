using System.Text.Json;
using MarkupString;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Theme;

/// <summary>
/// The game's layout themes: added ones, disabled built-in ones. Each test builds its own service over an in-memory
/// record, so the shared world's themes never change.
/// </summary>
public class LayoutThemeServiceTests
{
	private const string RedFantasy = "{\"preset\":\"fantasy\",\"colors\":{\"secondary\":\"#c0392b\"}}";

	[Test]
	public async Task AnAddedTheme_ReadsByName_AndIsListedAfterTheBuiltInOnes()
	{
		var service = new LayoutThemeService(new InMemoryData());

		var added = (await service.AddAsync("MyFantasy", RedFantasy)).Expect<LayoutThemeEntry>();

		await Assert.That(added.Name).IsEqualTo("myfantasy");
		await Assert.That(service.Read("myfantasy").Expect<ThemePalette>().Name).IsEqualTo("myfantasy");
		await Assert.That(service.Names.Last()).IsEqualTo("myfantasy");
		await Assert.That(service.Names.First()).IsEqualTo(LayoutThemes.Default);
	}

	[Test]
	public async Task ADisabledTheme_CannotBeNamed_ButAnAddedOneCanStartFromIt()
	{
		var service = new LayoutThemeService(new InMemoryData());

		await Assert.That((await service.SetDisabledAsync("fantasy", true)).Value is Success).IsTrue();
		var added = await service.AddAsync("myfantasy", RedFantasy);

		await Assert.That(service.Read("fantasy").Expect<Error<string>>().Value).IsEqualTo(LayoutThemes.Unknown);
		await Assert.That(service.Read("{\"preset\":\"fantasy\",\"mode\":\"light\"}").Expect<Error<string>>().Value).IsEqualTo(LayoutThemes.Unknown);
		await Assert.That(service.Names).DoesNotContain("fantasy");
		await Assert.That(added.Value is LayoutThemeEntry).IsTrue();
		await Assert.That(service.Read("myfantasy").Value is ThemePalette).IsTrue();
		await Assert.That(service.List().Single(theme => theme.Name == "fantasy").Disabled).IsTrue();

		await service.SetDisabledAsync("fantasy", false);
		await Assert.That(service.Read("fantasy").Value is ThemePalette).IsTrue();
	}

	[Test]
	public async Task AnAddedTheme_IsWrittenOutWhereItIsNamed()
	{
		var service = new LayoutThemeService(new InMemoryData());
		await service.AddAsync("myfantasy", RedFantasy);

		var resolved = service.Resolve("{\"preset\":\"myfantasy\",\"mode\":\"light\"}").Expect<string>();

		// What a connection is sent reads without the game's themes.
		await Assert.That(resolved).Contains("\"name\":\"myfantasy\"");
		await Assert.That(LayoutThemes.Read(resolved).Value is ThemePalette { Mode: ThemeMode.Light }).IsTrue();
		await Assert.That(service.Resolve("nord").Expect<string>()).IsEqualTo("nord");
	}

	[Test]
	public async Task SharpMUSHsOwnThemes_AreWrittenOutWhereTheyAreNamed()
	{
		var service = new LayoutThemeService(new InMemoryData());

		var named = service.Resolve("cyberpunk").Expect<string>();
		var moded = service.Resolve("{\"preset\":\"sharpmush\",\"colors\":{\"primary\":\"#ff0000\"}}").Expect<string>();

		// A connection's renderer knows only MarkupString's presets, so it is sent the colours.
		await Assert.That(ThemePalette.TryParse(named, out var cyberpunk, out _)).IsTrue();
		await Assert.That(cyberpunk!.Name).IsEqualTo("cyberpunk");
		await Assert.That(cyberpunk[ThemeRole.Primary]!.Value.Rgb!.Value.ToHex()).IsEqualTo("#ff2bd6");
		await Assert.That(ThemePalette.TryParse(moded, out var sharpmush, out _)).IsTrue();
		await Assert.That(sharpmush![ThemeRole.Primary]!.Value.Rgb!.Value.ToHex()).IsEqualTo("#ff0000");
		await Assert.That(sharpmush[ThemeRole.Secondary]!.Value.Rgb!.Value.ToHex()).IsEqualTo("#00f5b7");
	}

	[Test]
	public async Task SharpMUSHsOwnThemes_AreDisabledLikeTheOtherBuiltInOnes()
	{
		var service = new LayoutThemeService(new InMemoryData());

		await Assert.That((await service.SetDisabledAsync("idol", true)).Value is Success).IsTrue();

		await Assert.That(service.Read("idol").Expect<Error<string>>().Value).IsEqualTo(LayoutThemes.Unknown);
		await Assert.That(service.Names).DoesNotContain("idol");
		await Assert.That(service.List().Single(theme => theme.Name == "idol") is { BuiltIn: true, Disabled: true }).IsTrue();
	}

	[Test]
	public async Task AThemeBuiltOnAnAddedOne_KeepsItsCopy()
	{
		var service = new LayoutThemeService(new InMemoryData());
		await service.AddAsync("base", RedFantasy);
		await service.AddAsync("derived", "base");

		await service.RemoveAsync("base");

		await Assert.That(service.Read("base").Value is Error<string>).IsTrue();
		await Assert.That(service.Read("derived").Value is ThemePalette).IsTrue();
	}

	[Test]
	[Arguments("fantasy", RedFantasy, LayoutThemeService.BuiltInName)]
	[Arguments("cyberpunk", RedFantasy, LayoutThemeService.BuiltInName)]
	[Arguments("none", RedFantasy, LayoutThemeService.BuiltInName)]
	[Arguments("my theme", RedFantasy, LayoutThemeService.BadName)]
	[Arguments("mine", "nowhere", LayoutThemes.Unknown)]
	[Arguments("mine", "{\"seed\":\"blue\"}", "#-1 INVALID THEME: seed is a colour like #7aa2f7")]
	public async Task AddingABadTheme_SaysWhy(string name, string definition, string error)
		=> await Assert.That((await new LayoutThemeService(new InMemoryData()).AddAsync(name, definition)).Expect<Error<string>>().Value)
			.IsEqualTo(error);

	[Test]
	public async Task OnlyKnownThemes_AreRemovedOrDisabled()
	{
		var service = new LayoutThemeService(new InMemoryData());
		await service.AddAsync("mine", RedFantasy);

		await Assert.That((await service.RemoveAsync("nowhere")).Value is NotFound).IsTrue();
		await Assert.That((await service.SetDisabledAsync("nowhere", true)).Value is NotFound).IsTrue();
		await Assert.That((await service.SetDisabledAsync("mine", true)).Value is Error<string>).IsTrue();
	}

	[Test]
	public async Task TheThemes_AreReadBackFromStorage()
	{
		var data = new InMemoryData();
		var first = new LayoutThemeService(data);
		await first.AddAsync("mine", RedFantasy);
		await first.SetDisabledAsync("nord", true);

		var second = new LayoutThemeService(data);
		await second.LoadAsync();

		await Assert.That(second.Read("mine").Value is ThemePalette).IsTrue();
		await Assert.That(second.Read("nord").Value is Error<string>).IsTrue();
	}

	private sealed class InMemoryData : IExpandedObjectDataService
	{
		private readonly Dictionary<string, string> _server = [];

		public ValueTask<T?> GetExpandedDataAsync<T>(SharpObject obj) where T : class => throw new NotSupportedException();

		public ValueTask SetExpandedDataAsync<T>(T data, SharpObject obj, bool ignoreNull = false) where T : class
			=> throw new NotSupportedException();

		public ValueTask<T?> GetExpandedServerDataAsync<T>() where T : class
			=> ValueTask.FromResult(_server.TryGetValue(typeof(T).Name, out var json) ? JsonSerializer.Deserialize<T>(json) : null);

		public ValueTask SetExpandedServerDataAsync<T>(T data, bool ignoreNull = false) where T : class
		{
			_server[typeof(T).Name] = JsonSerializer.Serialize(data);
			return ValueTask.CompletedTask;
		}
	}
}
