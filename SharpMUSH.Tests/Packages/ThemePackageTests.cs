using System.Text;
using System.Text.Json;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Configuration;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Theme packages (<c>kind: theme</c>, format 1.21): the manifest's <c>themes:</c>, the stylesheets and pictures read
/// from the package, and installing, upgrading and removing them as themes of the game's.
/// </summary>
public class ThemePackageTests
{
	private const string Css = """
		:root { --texture: none; }
		.phosphor-shell.phosphor-shell { background-image: url("images/harbor.png"); }
		""";

	/// <summary>A PNG's first bytes, which is all the picture check reads.</summary>
	private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

	private static string Tokens(bool dark)
		=> string.Join("\n", (dark ? BuiltInThemes.Phosphor : BuiltInThemes.Daylight).Tokens
			.Where(t => ThemeTokens.Editable.Contains(t.Key))
			.Select(t => $"      {t.Key}: \"{t.Value}\""));

	private static string Manifest(string version = "1.0.0", string format = "\"1.21\"", string? themes = null, string extra = "")
		=> $"""
			format: {format}
			package: theme-harbor
			title: Harbor Lights
			version: {version}
			kind: theme
			category: Themes
			{extra}
			themes:
			{themes ?? Theme("harbor-night", "Harbor Night", dark: true, stylesheet: "harbor.css", images: ["images/harbor.png"])}
			""";

	private static string Theme(string id, string name, bool dark, string? stylesheet = null, string[]? images = null)
		=> $"""
			  - id: {id}
			    name: {name}
			    dark: {(dark ? "true" : "false")}
			    tokens:
			{Tokens(dark)}
			{(stylesheet is null ? "" : $"    stylesheet: {stylesheet}")}
			{(images is null ? "" : "    images:\n" + string.Join("\n", images.Select(i => $"      - {i}")))}
			""";

	private static PackageManifestResult<ParsedPackageManifest> Parse(string yaml) => new PackageManifestService().ParseManifest(yaml);

	private static IReadOnlyList<string> Errors(string yaml)
		=> Parse(yaml) is PackageManifestFailure failure
			? failure.Issues.Where(i => i.Severity == PackageManifestIssueSeverity.Error).Select(i => $"{i.Path}: {i.Message}").ToList()
			: [];

	private sealed class Files(Dictionary<string, byte[]> files) : IPackageFileSource
	{
		public Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken = default)
			=> Task.FromResult(files.TryGetValue(path, out var bytes) ? bytes : null);
	}

	private static Files Source(string css = Css, byte[]? picture = null, string pictureName = "images/harbor.png")
		=> new(new Dictionary<string, byte[]>
		{
			["harbor.css"] = Encoding.UTF8.GetBytes(css),
			[pictureName] = picture ?? Png
		});

	// ── The manifest ────────────────────────────────────────────────────────

	[Test]
	public async Task AThemePackageReadsItsThemes()
	{
		var manifest = Parse(Manifest()).Expect<ParsedPackageManifest>().Manifest;

		await Assert.That(manifest.Kind).IsEqualTo(PackageKind.Theme);
		await Assert.That(manifest.Contents).IsEqualTo(PackageContents.Theme);
		var theme = manifest.PortalThemes.Single();
		await Assert.That(theme.Id).IsEqualTo("harbor-night");
		await Assert.That(theme.Name).IsEqualTo("Harbor Night");
		await Assert.That(theme.Dark).IsTrue();
		await Assert.That(theme.StylesheetFile).IsEqualTo("harbor.css");
		await Assert.That(theme.Images.Select(i => i.File)).IsEquivalentTo(["images/harbor.png"]);
		await Assert.That(theme.Loaded).IsFalse();
	}

	[Test]
	public async Task ThemeKindNeedsFormat121()
		=> await Assert.That(Errors(Manifest(format: "\"1.20\""))).Contains("kind: 'kind: theme' needs format 1.21 or later; set format: \"1.21\".");

	[Test]
	public async Task ASoftcodePackageCannotShipThemes()
	{
		var yaml = Manifest().Replace("kind: theme\n", "") + "\nobjects:\n  - ref: core\n    type: thing\n    name: Core\n";

		await Assert.That(Errors(yaml)).Contains("themes: 'themes' is only valid when 'kind: theme'.");
	}

	[Test]
	public async Task AThemePackageShipsThemesOnly()
	{
		var errors = Errors(Manifest(extra: "settings:\n  money_name: Coin\nplugins: [scene]"));

		await Assert.That(errors).Contains("settings: A theme package ships themes only. Remove 'settings'.");
		await Assert.That(errors).Contains("plugins: A theme package ships themes only. Remove 'plugins'.");
		await Assert.That(errors.Count).IsEqualTo(2);
	}

	[Test]
	public async Task AThemePackageOwnsNoObjects()
		=> await Assert.That(Errors(Manifest() + "\nobjects:\n  - ref: core\n    type: thing\n    name: Core\n"))
			.Contains("objects: A theme package ships portal themes only — it owns no game objects. Remove 'objects'.");

	[Test]
	public async Task ATokenThatIsNotAColourIsRefused()
		=> await Assert.That(Errors(Manifest(themes: Theme("harbor-night", "Harbor Night", dark: true)
				.Replace($"bg: \"{BuiltInThemes.Phosphor.Tokens[ThemeTokens.Background]}\"", "bg: \"navy\""))))
			.Contains("themes[0].tokens: Token 'bg' is not a #rrggbb colour: 'navy'.");

	[Test]
	[Arguments("Harbor")]
	[Arguments("default")]
	[Arguments("9-lives")]
	public async Task AThemeIdIsALowerCaseSlug(string id)
		=> await Assert.That(Errors(Manifest(themes: Theme(id, "Harbor Night", dark: true))).Single())
			.StartsWith("themes[0].id:");

	[Test]
	public async Task TwoThemesCannotShareAnId()
		=> await Assert.That(Errors(Manifest(themes: Theme("harbor", "One", true) + "\n" + Theme("harbor", "Two", false))))
			.Contains("themes[1].id: Theme 'harbor' is listed twice.");

	[Test]
	public async Task AStylesheetOnAThemeThatIsNotCustomIsWarnedAbout()
	{
		var theme = Theme("harbor", "Harbor", true, "harbor.css")
			.Replace($"bg: \"{BuiltInThemes.Phosphor.Tokens[ThemeTokens.Background]}\"",
				$"bg: \"{BuiltInThemes.Phosphor.Tokens[ThemeTokens.Background]}\"\n      mode: simple");

		var parsed = Parse(Manifest(themes: theme)).Expect<ParsedPackageManifest>();

		await Assert.That(parsed.Warnings.Select(w => $"{w.Path}: {w.Message}")).Contains(
			"themes[0].tokens.mode: The stylesheet applies only to a custom theme, and this one's mode is 'simple'. Set mode: custom, or leave it out.");
	}

	[Test]
	public async Task APictureMustBeAnImageFileInThePackage()
		=> await Assert.That(Errors(Manifest(themes: Theme("harbor", "Harbor", true, "harbor.css", ["../outside.png", "notes.txt"]))).Count)
			.IsEqualTo(2);

	// ── The files ───────────────────────────────────────────────────────────

	[Test]
	public async Task LoadingReadsTheStylesheetAndPictures()
	{
		var manifest = Parse(Manifest()).Expect<ParsedPackageManifest>().Manifest;

		var loaded = (await PackageThemeFiles.LoadAsync(manifest, Source())).Expect<PackageManifest>();

		var theme = loaded.PortalThemes.Single();
		await Assert.That(theme.Loaded).IsTrue();
		await Assert.That(theme.Stylesheet).IsEqualTo(Css);
		await Assert.That(theme.Images.Single().Content).IsEquivalentTo(Png);
	}

	[Test]
	public async Task APictureThatIsNotWhatItsNameSaysIsRefused()
	{
		var manifest = Parse(Manifest()).Expect<ParsedPackageManifest>().Manifest;

		var error = (await PackageThemeFiles.LoadAsync(manifest, Source(picture: Encoding.UTF8.GetBytes("GIF89a....")))).Expect<Error<string>>();

		await Assert.That(error.Value).Contains("images/harbor.png is not the kind of picture its name says.");
	}

	[Test]
	public async Task AnSvgThatRunsScriptIsRefused()
	{
		var manifest = Parse(Manifest(themes: Theme("harbor-night", "Harbor Night", true, "harbor.css", ["images/harbor.svg"])))
			.Expect<ParsedPackageManifest>().Manifest;
		var svg = Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" onload="alert(1)"></svg>""");

		var error = (await PackageThemeFiles.LoadAsync(manifest,
			Source(Css.Replace("harbor.png", "harbor.svg"), svg, "images/harbor.svg"))).Expect<Error<string>>();

		await Assert.That(error.Value).Contains("images/harbor.svg holds script, which a theme's picture may not.");
	}

	[Test]
	[Arguments("""<svg xmlns="http://www.w3.org/2000/svg" xmlns:x="http://www.w3.org/2000/svg"><x:script>alert(1)</x:script></svg>""")]
	[Arguments("""<svg xmlns="http://www.w3.org/2000/svg" xmlns:l="http://www.w3.org/1999/xlink"><a l:href="java&#x73;cript&#58;alert(1)"><rect/></a></svg>""")]
	[Arguments("""<svg xmlns="http://www.w3.org/2000/svg"><foreignObject><div/></foreignObject></svg>""")]
	[Arguments("""<svg xmlns="http://www.w3.org/2000/svg"><a href="https://example.com/"><rect/></a></svg>""")]
	[Arguments("""<!DOCTYPE svg [<!ENTITY s "script">]><svg xmlns="http://www.w3.org/2000/svg"></svg>""")]
	[Arguments("""<svg xmlns="http://www.w3.org/2000/svg"><rect></svg>""")]
	public async Task SvgThatCouldRunScriptLooksActive(string svg)
		=> await Assert.That(SvgContent.LooksActive(svg)).IsTrue();

	[Test]
	public async Task APlainSvgPictureIsNotActive()
		=> await Assert.That(SvgContent.LooksActive(
				"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><defs><linearGradient id="g"/></defs><rect fill="url(#g)" width="10" height="10"/><use href="#g"/></svg>"""))
			.IsFalse();

	[Test]
	public async Task AThemeFilePathCannotHoldCharactersThatEndAUrl()
		=> await Assert.That(Errors(Manifest(themes: Theme("harbor", "Harbor", true, "harbor.css", ["images/a\"b.png", "images/a*b.png"]))).Count)
			.IsEqualTo(2);

	[Test]
	public async Task AStylesheetMayNameOnlyThePicturesTheThemeCarries()
	{
		var manifest = Parse(Manifest()).Expect<ParsedPackageManifest>().Manifest;

		var error = (await PackageThemeFiles.LoadAsync(manifest,
			Source(Css + ".kit-card.kit-card { background: url(images/other.png); }"))).Expect<Error<string>>();

		await Assert.That(error.Value).Contains("url(images/other.png) names a picture the theme does not list in 'images'.");
	}

	[Test]
	public async Task AStylesheetIsCheckedAsAStaffOneIs()
	{
		var manifest = Parse(Manifest()).Expect<ParsedPackageManifest>().Manifest;

		var error = (await PackageThemeFiles.LoadAsync(manifest,
			Source(Css + "body { background: url(https://example.com/x.png); }"))).Expect<Error<string>>();

		await Assert.That(error.Value).Contains("loads from elsewhere");
	}

	[Test]
	public async Task AMissingFileIsNamed()
	{
		var manifest = Parse(Manifest()).Expect<ParsedPackageManifest>().Manifest;

		var error = (await PackageThemeFiles.LoadAsync(manifest, new Files([]))).Expect<Error<string>>();

		await Assert.That(error.Value).Contains("harbor-night: images/harbor.png is not in the package.");
		await Assert.That(error.Value).Contains("harbor-night: harbor.css is not in the package.");
	}

	// ── Installing ──────────────────────────────────────────────────────────

	private sealed class World : IDisposable
	{
		public Dictionary<string, InstalledPackageRecord> Installed { get; } = [];
		public List<PackageRevisionRecord> Revisions { get; } = [];
		public IPackageInstallService Inner { get; } = Substitute.For<IPackageInstallService>();
		public PortalThemeService Themes { get; } = new(new InMemoryData());
		public string AssetRoot { get; } = Path.Join(Path.GetTempPath(), "theme-package-tests-" + Guid.NewGuid().ToString("N"));
		public FileSystemWikiAssetService Assets { get; }
		public ThemePackageInstallService Installer { get; }

		public World()
		{
			Assets = new FileSystemWikiAssetService(AssetRoot);
			var registry = Substitute.For<IPackageRegistryService>();
			registry.GetInstalledPackageAsync(Arg.Any<string>()).Returns(call =>
				Task.FromResult(Installed.TryGetValue(call.Arg<string>(), out var found) ? new Found<InstalledPackageRecord>(found) : new NotFound()));
			registry.GetInstalledPackagesAsync().Returns(_ => Task.FromResult<IReadOnlyList<InstalledPackageRecord>>(Installed.Values.ToList()));
			registry.GetPackageDependentsAsync(Arg.Any<string>()).Returns(Task.FromResult<IReadOnlyList<PackageDependencyRecord>>([]));
			registry.When(r => r.UpsertInstalledPackageAsync(Arg.Any<InstalledPackageRecord>()))
				.Do(call => Installed[call.Arg<InstalledPackageRecord>().Id] = call.Arg<InstalledPackageRecord>());
			registry.When(r => r.RemoveInstalledPackageAsync(Arg.Any<string>())).Do(call => Installed.Remove(call.Arg<string>()));
			registry.When(r => r.AddPackageRevisionAsync(Arg.Any<PackageRevisionRecord>())).Do(call => Revisions.Add(call.Arg<PackageRevisionRecord>()));
			var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
			options.CurrentValue.Returns(ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst"));
			Installer = new ThemePackageInstallService(Inner, registry, new PackageOperationGate(), Themes, Assets,
				new EngineVersion(new PackageVersion(1, 1, 0)), options);
		}

		public async Task<Result<PackageApplyResult>> ApplyAsync(string yaml, Files? files = null)
			=> await Installer.ApplyAsync(Parse(yaml).Expect<ParsedPackageManifest>().Manifest,
				new PackageApplyRequest(new PackageApplySource("file:///srv/themes", "theme-harbor/", "abc123", "main"),
					new Dictionary<string, string>(), []), fileSource: files ?? Source());

		public async Task<IReadOnlyList<WikiAsset>> StoredPicturesAsync() => await Assets.ListAsync();

		public void Dispose()
		{
			if (Directory.Exists(AssetRoot)) Directory.Delete(AssetRoot, recursive: true);
		}
	}

	[Test]
	public async Task InstallingAddsTheThemesForPlayersToChoose()
	{
		using var world = new World();

		var applied = (await world.ApplyAsync(Manifest())).Expect<PackageApplyResult>();

		await Assert.That(applied.Notes).Contains("Added theme Harbor Night. Players can choose it now.");
		var offered = (await world.Themes.GetThemesAsync(includeUnpublished: false)).Themes.Single(t => t.Id == "harbor-night");
		await Assert.That(offered.Package).IsEqualTo("theme-harbor");
		await Assert.That(offered.PackageVersion).IsEqualTo("1.0.0");
		await Assert.That(offered.Dark).IsTrue();
		await Assert.That(world.Installed["theme-harbor"].Kind).IsEqualTo(PackageKind.Theme);
		await Assert.That(world.Revisions.Single().Kind).IsEqualTo(PackageRevisionKind.Install);
	}

	[Test]
	public async Task TheStylesheetShowsThePictureWhereTheGameServesIt()
	{
		using var world = new World();

		await world.ApplyAsync(Manifest());

		var picture = (await world.StoredPicturesAsync()).Single();
		var theme = (await world.Themes.GetThemesAsync(includeUnpublished: false)).Themes.Single(t => t.Id == "harbor-night");
		await Assert.That(theme.Stylesheet).Contains($"url(\"/api/wiki-assets/{picture.Id}/harbor.png\")");
		await Assert.That(ThemeStylesheet.Validate(theme.Stylesheet)).IsEmpty();
		await Assert.That(picture.Private).IsTrue();
		var opened = (await world.Assets.OpenAsync(picture.Id)).Expect<OpenedWikiAsset>();
		await using var content = opened.Content;
		using var copy = new MemoryStream();
		await content.CopyToAsync(copy);
		await Assert.That(copy.ToArray()).IsEquivalentTo(Png);
	}

	[Test]
	public async Task AnUpgradeReplacesThemesAndDropsWhatTheVersionNoLongerShips()
	{
		using var world = new World();
		await world.ApplyAsync(Manifest(themes: Theme("harbor-night", "Harbor Night", true, "harbor.css", ["images/harbor.png"])
			+ "\n" + Theme("harbor-day", "Harbor Day", false)));
		var first = (await world.StoredPicturesAsync()).Single();

		var applied = (await world.ApplyAsync(Manifest("1.1.0", themes: Theme("harbor-night", "Harbor Night", true, "harbor.css", ["images/harbor.png"]))))
			.Expect<PackageApplyResult>();

		await Assert.That(applied.Notes).Contains("Updated theme Harbor Night.");
		await Assert.That(applied.Notes).Contains("Removed theme Harbor Day. Characters who chose it see the game's default instead.");
		var themes = (await world.Themes.GetThemesAsync(includeUnpublished: true)).Themes;
		await Assert.That(themes.Any(t => t.Id == "harbor-day")).IsFalse();
		await Assert.That(themes.Single(t => t.Id == "harbor-night").PackageVersion).IsEqualTo("1.1.0");
		var stored = await world.StoredPicturesAsync();
		await Assert.That(stored.Count).IsEqualTo(1).Because("the previous version's copy of the picture is deleted");
		await Assert.That(stored.Single().Id).IsNotEqualTo(first.Id);
		await Assert.That(world.Revisions.Last().Kind).IsEqualTo(PackageRevisionKind.Upgrade);
	}

	[Test]
	public async Task AThemeWhoseIdTheGameAlreadyUsesIsBlocked()
	{
		using var world = new World();
		var tokens = BuiltInThemes.Phosphor.Tokens.ToDictionary();
		await world.Themes.CreateAsync(new PortalThemeRequest("Harbor Night", Dark: true, Published: true, tokens));

		var plan = await world.Installer.PlanAsync((await PackageThemeFiles.LoadAsync(
			Parse(Manifest()).Expect<ParsedPackageManifest>().Manifest, Source())).Expect<PackageManifest>());
		var error = (await world.ApplyAsync(Manifest())).Expect<Error<string>>();

		await Assert.That(plan.IsBlocked).IsTrue();
		await Assert.That(plan.Themes!.Single().Detail).IsEqualTo("The game's own theme Harbor Night has the id 'harbor-night'.");
		await Assert.That(error.Value).IsEqualTo("Plan is blocked: theme harbor-night: The game's own theme Harbor Night has the id 'harbor-night'.");
		await Assert.That(await world.StoredPicturesAsync()).IsEmpty();
		await Assert.That(world.Installed).IsEmpty();
	}

	[Test]
	public async Task ThePlanShowsEachThemeForReview()
	{
		using var world = new World();
		var manifest = (await PackageThemeFiles.LoadAsync(Parse(Manifest()).Expect<ParsedPackageManifest>().Manifest, Source()))
			.Expect<PackageManifest>();

		var plan = await world.Installer.PlanAsync(manifest);

		await Assert.That(plan.IsBlocked).IsFalse();
		await Assert.That(plan.Kind).IsEqualTo(PackageRevisionKind.Install);
		var change = plan.Themes!.Single();
		await Assert.That(change.Action).IsEqualTo(PackageThemeAction.Add);
		await Assert.That(change.Images).IsEqualTo(1);
		await Assert.That(change.StylesheetLength).IsEqualTo(Css.Length);
		await Assert.That(change.Tokens![ThemeTokens.Background]).IsEqualTo(BuiltInThemes.Phosphor.Tokens[ThemeTokens.Background]);
	}

	[Test]
	public async Task AnUpgradeCannotDropTheDefaultTheme()
	{
		using var world = new World();
		await world.ApplyAsync(Manifest(themes: Theme("harbor-night", "Harbor Night", true) + "\n" + Theme("harbor-day", "Harbor Day", false)));
		await world.Themes.SetDefaultAsync("harbor-day");

		var error = (await world.ApplyAsync(Manifest("1.1.0", themes: Theme("harbor-night", "Harbor Night", true)))).Expect<Error<string>>();

		await Assert.That(error.Value).Contains("theme harbor-day: It is the game's default for browsers that prefer light, and it would be removed.");
		await Assert.That(world.Installed["theme-harbor"].Version).IsEqualTo("1.0.0");
	}

	[Test]
	public async Task AStaffCopyOfAPackagedThemeKeepsItsPictureAfterUninstall()
	{
		using var world = new World();
		await world.ApplyAsync(Manifest());
		var packaged = (await world.Themes.GetThemesAsync(includeUnpublished: true)).Themes.Single(t => t.Id == "harbor-night");
		(await world.Themes.CreateAsync(new PortalThemeRequest("Harbor Copy", true, true,
			new Dictionary<string, string>(packaged.Tokens), packaged.Stylesheet))).Expect<PortalTheme>();

		(await world.Installer.UninstallAsync("theme-harbor")).Expect<Success>();

		await Assert.That((await world.StoredPicturesAsync()).Count).IsEqualTo(1).Because("the copy's stylesheet still shows it");
	}

	[Test]
	public async Task UninstallingWhileAThemeIsTheDefaultIsRefused()
	{
		using var world = new World();
		await world.ApplyAsync(Manifest());
		await world.Themes.SetDefaultAsync("harbor-night");

		var refused = (await world.Installer.UninstallAsync("theme-harbor")).Expect<Error<string>>();

		await Assert.That(refused.Value).IsEqualTo("Cannot uninstall 'theme-harbor': its theme Harbor Night is the game's default for browsers "
			+ "that prefer dark. Make another theme the default first, or force-remove it and Phosphor takes its place.");
		await Assert.That((await world.Themes.GetThemesAsync(false)).DefaultThemeId).IsEqualTo("harbor-night");
		await Assert.That(world.Installed.ContainsKey("theme-harbor")).IsTrue();
	}

	[Test]
	public async Task ForceRemovingTheDefaultGivesItsPlaceBackToTheBuiltInTheme()
	{
		using var world = new World();
		await world.ApplyAsync(Manifest());
		await world.Themes.SetDefaultAsync("harbor-night");

		(await world.Installer.UninstallAsync("theme-harbor", force: true)).Expect<Success>();

		var state = await world.Themes.GetThemesAsync(false);
		await Assert.That(state.DefaultThemeId).IsEqualTo(BuiltInThemes.PhosphorId);
		await Assert.That((await world.Themes.GetStoredAsync()).DefaultThemeId).IsNull();
	}

	[Test]
	public async Task UninstallingRemovesTheThemesAndTheirPictures()
	{
		using var world = new World();
		await world.ApplyAsync(Manifest());

		(await world.Installer.UninstallAsync("theme-harbor")).Expect<Success>();

		await Assert.That((await world.Themes.GetThemesAsync(true)).Themes.Any(t => t.Package is not null)).IsFalse();
		await Assert.That(await world.StoredPicturesAsync()).IsEmpty();
		await Assert.That(world.Installed).IsEmpty();
		await world.Inner.DidNotReceiveWithAnyArgs().UninstallAsync(default!);
	}

	[Test]
	public async Task AThemePackageCannotBeRolledBack()
	{
		using var world = new World();
		await world.ApplyAsync(Manifest());

		var refused = (await world.Installer.RollbackAsync("theme-harbor", 1)).Expect<Error<string>>();

		await Assert.That(refused.Value).StartsWith("Cannot roll back 'theme-harbor': a theme package's revisions do not keep");
	}

	[Test]
	public async Task StaffCannotEditOrDeleteAPackagedTheme()
	{
		using var world = new World();
		await world.ApplyAsync(Manifest());
		var tokens = BuiltInThemes.Phosphor.Tokens.ToDictionary();

		var edit = (await world.Themes.UpdateAsync("harbor-night", new PortalThemeRequest("Harbor", true, true, tokens))).Expect<Error<string>>();
		var delete = (await world.Themes.DeleteAsync("harbor-night")).Expect<Error<string>>();

		await Assert.That(edit.Value).IsEqualTo(
			"Harbor Night comes from the theme-harbor package, which is the only thing that changes it. Duplicate it to make one of your own.");
		await Assert.That(delete.Value).IsEqualTo("Harbor Night comes from the theme-harbor package. Uninstall the package to remove it.");
	}

	[Test]
	public async Task OtherKindsGoToThePackageInstaller()
	{
		using var world = new World();
		var manifest = new PackageManifestService().ParseManifest("""
			format: "1.21"
			package: plain
			title: Plain
			version: 1.0.0
			objects:
			  - ref: core
			    type: thing
			    name: Core
			""").Expect<ParsedPackageManifest>().Manifest;
		var request = new PackageApplyRequest(new PackageApplySource("r", "p", "c", "b"), new Dictionary<string, string>(), []);
		world.Inner.ApplyAsync(manifest, request, Arg.Any<CancellationToken>(), null)
			.Returns(new PackageApplyResult(1, new Dictionary<string, string>(), []));

		(await world.Installer.ApplyAsync(manifest, request)).Expect<PackageApplyResult>();
		await world.Inner.Received(1).ApplyAsync(manifest, request, Arg.Any<CancellationToken>(), null);
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
