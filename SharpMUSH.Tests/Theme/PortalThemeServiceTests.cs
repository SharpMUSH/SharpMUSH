using System.Text.Json;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Theme;

/// <summary>
/// Staff-made themes and each character's choice. The theme tests build their own service over an in-memory record,
/// so the shared world's themes never change; the appearance test uses the real store on a character of its own.
/// </summary>
public class PortalThemeServiceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private static PortalThemeRequest Request(string name, bool published = true, Action<Dictionary<string, string>>? edit = null)
	{
		var tokens = BuiltInThemes.Phosphor.Tokens.ToDictionary();
		edit?.Invoke(tokens);
		return new PortalThemeRequest(name, Dark: true, published, tokens);
	}

	[Test]
	public async Task AFreshGameOffersTheBuiltInThemesWithPhosphorAndDaylightAsDefaults()
	{
		var themes = await new PortalThemeService(new InMemoryData()).GetThemesAsync(includeUnpublished: false);

		await Assert.That(themes.Themes.Select(t => t.Id)).IsEquivalentTo(BuiltInThemes.All.Select(t => t.Id));
		await Assert.That(themes.DefaultThemeId).IsEqualTo("phosphor");
		await Assert.That(themes.DefaultLightThemeId).IsEqualTo("daylight");
	}

	[Test]
	public async Task ALightThemeBecomesTheDefaultForLightBrowsersOnly()
	{
		var service = new PortalThemeService(new InMemoryData());
		var light = (await service.CreateAsync(Request("Dawn") with { Dark = false })).Expect<PortalTheme>();

		var state = (await service.SetDefaultAsync(light.Id)).Expect<PortalThemesResponse>();

		await Assert.That(state.DefaultLightThemeId).IsEqualTo(light.Id);
		await Assert.That(state.DefaultThemeId).IsEqualTo("phosphor");
		await Assert.That((await service.UpdateAsync(light.Id, Request("Dawn"))).Value is Error<string> { Value: var why } && why.Contains("light or dark"))
			.IsTrue().Because("a default keeps its mode");
	}

	[Test]
	public async Task AThemesStyleIsStoredWithItAndAnUnknownChoiceRefused()
	{
		var service = new PortalThemeService(new InMemoryData());

		var created = (await service.CreateAsync(Request("Ink", edit: t => t[ThemeStyles.FontDisplay] = "cinzel"))).Expect<PortalTheme>();
		await Assert.That(created.Tokens[ThemeStyles.FontDisplay]).IsEqualTo("cinzel");
		await Assert.That(created.Tokens[ThemeStyles.Texture]).IsEqualTo("none");

		var refused = await service.CreateAsync(Request("Bad", edit: t => t[ThemeStyles.Ornament] = "skull"));
		await Assert.That(refused.Value).IsTypeOf<Error<string>>();
	}

	[Test]
	public async Task ACreatedThemeGetsAnIdFromItsNameAndNormalisedColours()
	{
		var service = new PortalThemeService(new InMemoryData());

		var created = (await service.CreateAsync(Request("Crimson Night!", edit: t => t["accent"] = "#F5A"))).Expect<PortalTheme>();
		var again = (await service.CreateAsync(Request("Crimson  Night"))).Expect<PortalTheme>();

		await Assert.That(created.Id).IsEqualTo("crimson-night");
		await Assert.That(created.Tokens["accent"]).IsEqualTo("#ff55aa");
		await Assert.That(again.Id).IsEqualTo("crimson-night-2");
	}

	[Test]
	public async Task AThemeNamedDefaultDoesNotTakeTheDefaultRoute()
	{
		var created = (await new PortalThemeService(new InMemoryData()).CreateAsync(Request("Default"))).Expect<PortalTheme>();

		await Assert.That(created.Id).IsNotEqualTo("default");
	}

	[Test]
	[Arguments("")]
	[Arguments("phosphor")]
	[Arguments("This name is far longer than forty characters allow")]
	public async Task ABadOrTakenNameIsRefused(string name)
	{
		var result = await new PortalThemeService(new InMemoryData()).CreateAsync(Request(name));

		await Assert.That(result.Value).IsTypeOf<Error<string>>();
	}

	[Test]
	public async Task AThemeMissingATokenIsRefused()
	{
		var result = await new PortalThemeService(new InMemoryData()).CreateAsync(Request("Holey", edit: t => t.Remove("surface")));

		await Assert.That(result.Value is Error<string> { Value: var message } && message.Contains("'surface'")).IsTrue();
	}

	[Test]
	public async Task ABuiltInThemeCannotBeChangedOrDeleted()
	{
		var service = new PortalThemeService(new InMemoryData());

		await Assert.That((await service.UpdateAsync("phosphor", Request("Phosphor"))).Value).IsTypeOf<Error<string>>();
		await Assert.That((await service.DeleteAsync("daylight")).Value).IsTypeOf<Error<string>>();
	}

	[Test]
	public async Task AnUnpublishedThemeIsHiddenFromPlayersAndCannotBeTheDefault()
	{
		var service = new PortalThemeService(new InMemoryData());
		var draft = (await service.CreateAsync(Request("Draft", published: false))).Expect<PortalTheme>();

		await Assert.That((await service.GetThemesAsync(includeUnpublished: false)).Themes.Any(t => t.Id == draft.Id)).IsFalse();
		await Assert.That((await service.GetThemesAsync(includeUnpublished: true)).Themes.Any(t => t.Id == draft.Id)).IsTrue();
		await Assert.That((await service.SetDefaultAsync(draft.Id)).Value).IsTypeOf<Error<string>>();
	}

	[Test]
	public async Task TheDefaultStaysPublishedAndCannotBeDeleted()
	{
		var service = new PortalThemeService(new InMemoryData());
		var theme = (await service.CreateAsync(Request("House"))).Expect<PortalTheme>();
		(await service.SetDefaultAsync(theme.Id)).Expect<PortalThemesResponse>();

		await Assert.That((await service.UpdateAsync(theme.Id, Request("House", published: false))).Value).IsTypeOf<Error<string>>();
		await Assert.That((await service.DeleteAsync(theme.Id)).Value).IsTypeOf<Error<string>>();

		(await service.SetDefaultAsync("horror")).Expect<PortalThemesResponse>();
		var left = (await service.DeleteAsync(theme.Id)).Expect<PortalThemesResponse>();
		await Assert.That(left.Themes.Any(t => t.Id == theme.Id)).IsFalse();
		await Assert.That(left.DefaultThemeId).IsEqualTo("horror");
	}

	[Test]
	public async Task AnEditKeepsTheIdAndReplacesTheRest()
	{
		var service = new PortalThemeService(new InMemoryData());
		var theme = (await service.CreateAsync(Request("Moss"))).Expect<PortalTheme>();

		var edited = (await service.UpdateAsync(theme.Id, Request("Deep Moss", edit: t => t["bg"] = "#001100"))).Expect<PortalTheme>();

		await Assert.That(edited.Id).IsEqualTo(theme.Id);
		await Assert.That(edited.Name).IsEqualTo("Deep Moss");
		await Assert.That((await service.GetThemesAsync(true)).Themes.Single(t => t.Id == theme.Id).Tokens["bg"]).IsEqualTo("#001100");
		await Assert.That((await service.UpdateAsync("nope", Request("Nope"))).Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task ACharactersThemeAndAccentAreStoredWithIt()
	{
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var themes = WebAppFactoryArg.Services.GetRequiredService<IPortalThemeService>();
		var playerRef = await mediator.Send(new CreatePlayerCommand(TestIsolationHelpers.GenerateUniqueName("Theme"), "password",
			new DBRef(0), new DBRef(0), 100));
		var player = (await mediator.Send(new GetObjectNodeQuery(playerRef))).Expect<AnySharpObject>().Object();

		await Assert.That(await themes.GetAppearanceAsync(player)).IsEqualTo(new CharacterAppearance(null, null));

		var stored = (await themes.SetAppearanceAsync(player, new CharacterAppearance("daylight", "#FFB454"))).Expect<CharacterAppearance>();
		await Assert.That(stored).IsEqualTo(new CharacterAppearance("daylight", "#ffb454"));
		await Assert.That(await themes.GetAppearanceAsync(player)).IsEqualTo(stored);

		await Assert.That((await themes.SetAppearanceAsync(player, new CharacterAppearance("no-such-theme", null))).Value).IsTypeOf<Error<string>>();
		await Assert.That((await themes.SetAppearanceAsync(player, new CharacterAppearance(null, "teal"))).Value).IsTypeOf<Error<string>>();
		await Assert.That(await themes.GetAppearanceAsync(player)).IsEqualTo(stored);

		(await themes.SetAppearanceAsync(player, new CharacterAppearance(null, null))).Expect<CharacterAppearance>();
		await Assert.That(await themes.GetAppearanceAsync(player)).IsEqualTo(new CharacterAppearance(null, null));
	}

	/// <summary>Server data round-tripped through JSON, as the database stores it.</summary>
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
