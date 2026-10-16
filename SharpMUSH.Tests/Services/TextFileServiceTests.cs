using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class TextFileServiceTests
{
	[Test]
	public async Task CanonicalResolutionSearchScopeLocalizationAndRevision()
	{
		var (service, root) = BuildServiceOverTempFiles(0, 0);
		Directory.CreateDirectory(Path.Join(root, "help"));
		Directory.CreateDirectory(Path.Join(root, "ahelp"));
		var path = Path.Join(root, "help", "align.md");
		const string article = """
			<!-- help-article
			{"corpus":"help","id":"align","lookup":"align()","aliases":["lalign()"],
			 "sections":[{"id":"examples","heading":"Examples","lookup":"align examples"}],
			 "redirects":{"ALIGN2":"align examples"}}
			-->
			# align()
			Overview.
			## Examples
			Needle example.
			""";
		try
		{
			await File.WriteAllTextAsync(path, article);
			await File.WriteAllTextAsync(Path.Join(root, "ahelp", "security.md"), "# security\nAdministrator only.");
			await service.ReindexAsync();
			var resolver = new HelpTopicResolver(service);
			var alias = await resolver.GetExactAsync("help", "LALIGN()");
			await Assert.That(alias?.Topic).IsEqualTo("align()");
			await Assert.That(alias?.Article?.Id).IsEqualTo("align");
			await Assert.That(alias?.Markdown).DoesNotContain("Needle");
			var redirect = await resolver.GetExactAsync("help", "ALIGN2");
			await Assert.That(redirect?.Topic).IsEqualTo("align examples");
			await Assert.That(redirect?.SectionId).IsEqualTo("examples");
			await Assert.That(await resolver.ListTopicsAsync("help"))
				.IsEquivalentTo(new[] { "align()", "align examples" });
			await Assert.That(await resolver.SearchTopicsAsync("help", "lalign*"))
				.IsEquivalentTo(new[] { "align()" });
			await Assert.That(await resolver.SearchTopicsAsync("help", "*align*"))
				.IsEquivalentTo(new[] { "align()", "align examples" });
			var fuzzy = (await resolver.ResolveAsync("help", "al examples")).Expect<HelpEntry>();
			await Assert.That(fuzzy.Topic).IsEqualTo("align examples");
			await Assert.That(await resolver.SearchContentAsync("help", "Needle"))
				.IsEquivalentTo(new[] { "align examples" });
			await Assert.That(await resolver.GetExactAsync("help", "security")).IsNull();
			await Assert.That(await resolver.GetExactAsync("help.fr", "security")).IsNull();
			var localized = new LocalizedTextFileService(service);
			await Assert.That(await localized.GetEntryAsync("help", "align()", "fr")).IsEqualTo(alias?.Markdown);
			await File.WriteAllTextAsync(path, article.Replace("Overview.", "Revised overview."));
			await Assert.That((await service.GetEntryAsync("help", "align()"))!).DoesNotContain("Revised");
			await service.ReindexAsync();
			await Assert.That((await service.GetEntryAsync("help", "align()"))!).Contains("Revised");
			Directory.CreateDirectory(Path.Join(root, "help.fr"));
			await File.WriteAllTextAsync(Path.Join(root, "help.fr", "align.md"), article.Replace("Overview.", "Vue française."));
			await File.WriteAllTextAsync(Path.Join(root, "help", "prefix.md"), "# zulu\n# a0alias\nAlpha.\n# a1topic\nBeta.");
			await service.ReindexAsync();
			var prefix = (await resolver.ResolveAsync("help", "a")).Expect<HelpEntry>();
			await Assert.That(prefix.Topic).IsEqualTo("zulu");
			await Assert.That(await localized.GetEntryAsync("help", "align()", "fr")).Contains("Vue française");
			await Assert.That((await service.GetHelpEntryAsync("help.fr", "align()"))?.Article?.Id).IsEqualTo("align");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>
	/// A loaded plugin's own help is indexed with the engine's; a plugin that did not load shows none, and a plugin
	/// topic that clashes with the engine's is left out rather than stopping the index.
	/// </summary>
	[Test]
	public async Task LoadedPluginHelp_IsIndexed_AndNeverDisplacesTheEngines()
	{
		var plugins = Path.Join(Path.GetTempPath(), $"sharpmush-plugins-{Guid.NewGuid():N}");
		string Plugin(string id, string file, string markdown)
		{
			var help = Path.Join(plugins, id, "help");
			Directory.CreateDirectory(help);
			File.WriteAllText(Path.Join(help, file), markdown);
			return Path.Join(plugins, id);
		}

		var catalog = PluginCatalog.ForEntries(
		[
			new PluginBootEntry("radio", null, "1.0.0", null, PluginOrigin.BuiltIn,
				Plugin("radio", "radio.md", "# @radio\nTunes in."), PluginBootStatus.Loaded, null),
			new PluginBootEntry("clash", null, "1.0.0", null, PluginOrigin.BuiltIn,
				Plugin("clash", "clash.md", """
					<!-- help-article
					{"corpus":"help","id":"clash","lookup":"@clash","aliases":["engine"]}
					-->
					# @clash
					Taken over.
					"""), PluginBootStatus.Loaded, null),
			new PluginBootEntry("off", null, "1.0.0", null, PluginOrigin.BuiltIn,
				Plugin("off", "off.md", "# @off\nNot loaded."), PluginBootStatus.Disabled, null),
			new PluginBootEntry("legacy", null, "1.0.0", null, PluginOrigin.BuiltIn,
				Plugin("legacy", "legacy.md", "# engine\nStolen.\n\n# @legacyonly\nOnly here."), PluginBootStatus.Loaded, null),
		]);
		var (service, root) = BuildServiceOverTempFiles(0, 0, catalog);
		try
		{
			Directory.CreateDirectory(Path.Join(root, "help"));
			await File.WriteAllTextAsync(Path.Join(root, "help", "engine.md"), "# engine\nThe engine's own.");
			await service.ReindexAsync();

			await Assert.That(await service.GetEntryAsync("help", "@radio")).Contains("Tunes in.");
			await Assert.That(await service.GetEntryAsync("help", "@off")).IsNull().Because("the plugin is not loaded");
			await Assert.That(await service.GetEntryAsync("help", "engine")).Contains("The engine's own.");
			await Assert.That(await service.GetEntryAsync("help", "@clash")).IsNull()
				.Because("a file with a clashing topic is left out whole");
			await Assert.That(await service.GetEntryAsync("help", "@legacyonly")).IsNull()
				.Because("an old-style file with a clashing topic is left out whole too");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
			Directory.Delete(plugins, recursive: true);
		}
	}

	/// <summary>
	/// A running plugin's topics are listed on <c>plugin help</c> under its name, and on each index its
	/// <c>listedIn</c> names: an article's overview, or the section the index lookup names. An index that does not
	/// exist is skipped, and the plugin's topic is still indexed.
	/// </summary>
	[Test]
	public async Task PluginHelp_IsListedOnPluginHelp_AndOnTheIndexesItNames()
	{
		var plugins = Path.Join(Path.GetTempPath(), $"sharpmush-plugins-{Guid.NewGuid():N}");
		var help = Path.Join(plugins, "radio", "help");
		Directory.CreateDirectory(help);
		await File.WriteAllTextAsync(Path.Join(help, "radio.md"), """
			<!-- help-article
			{"corpus":"help","id":"radio","lookup":"@radio","aliases":[],"listedIn":["@-general","@-missing"],
			 "sections":[{"id":"tune","heading":"Tuning","lookup":"tune()","listedIn":["string functions"]}]}
			-->
			# @radio
			Tunes in.
			## Tuning
			Picks a station.
			""");
		var catalog = PluginCatalog.ForEntries(
		[
			new PluginBootEntry("radio", "Radio", "1.0.0", null, PluginOrigin.BuiltIn, Path.Join(plugins, "radio"), PluginBootStatus.Loaded, null),
		]);
		var (service, root) = BuildServiceOverTempFiles(0, 0, catalog);
		Directory.CreateDirectory(Path.Join(root, "help"));
		try
		{
			await File.WriteAllTextAsync(Path.Join(root, "help", "plugins.md"), "# plugin help\nPlugins add these.");
			await File.WriteAllTextAsync(Path.Join(root, "help", "general.md"), "# @-general\nGeneral commands.");
			await File.WriteAllTextAsync(Path.Join(root, "help", "functions.md"), """
				<!-- help-article
				{"corpus":"help","id":"functions","lookup":"function list","aliases":[],
				 "sections":[{"id":"string","heading":"String functions","lookup":"string functions"}]}
				-->
				# Function list
				Categories.
				## String functions
				Text.
				""");
			await service.ReindexAsync();

			await Assert.That(await service.GetEntryAsync("help", "plugin help")).Contains("Radio: [@radio]");
			await Assert.That(await service.GetEntryAsync("help", "@-general")).Contains("From plugins: [@radio]");
			await Assert.That(await service.GetEntryAsync("help", "string functions")).Contains("From plugins: [tune()]");
			await Assert.That(await service.GetEntryAsync("help", "function list")).DoesNotContain("tune()")
				.Because("the listing goes in the section it names");
			await Assert.That(await service.GetEntryAsync("help", "@radio")).Contains("Tunes in.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
			Directory.Delete(plugins, recursive: true);
		}
	}

	/// <summary>A plugin unloaded since the start keeps its catalog entry, but not its help.</summary>
	[Test]
	public async Task UnloadedPluginHelp_IsNotIndexed()
	{
		var plugins = Path.Join(Path.GetTempPath(), $"sharpmush-plugins-{Guid.NewGuid():N}");
		var help = Path.Join(plugins, "radio", "help");
		Directory.CreateDirectory(help);
		await File.WriteAllTextAsync(Path.Join(help, "radio.md"), "# @radio\nTunes in.");
		var catalog = PluginCatalog.ForEntries(
		[
			new PluginBootEntry("radio", null, "1.0.0", null, PluginOrigin.BuiltIn, Path.Join(plugins, "radio"), PluginBootStatus.Loaded, null),
		]);
		var manager = Substitute.For<IPluginManager>();
		var (service, root) = BuildServiceOverTempFiles(0, 0, catalog, new Lazy<IPluginManager>(() => manager));
		Directory.CreateDirectory(root);
		try
		{
			await service.ReindexAsync();
			await Assert.That(await service.GetEntryAsync("help", "@radio")).Contains("Tunes in.");

			manager.WasUnloaded("radio").Returns(true);
			await service.ReindexAsync();
			await Assert.That(await service.GetEntryAsync("help", "@radio")).IsNull();
		}
		finally
		{
			Directory.Delete(root, recursive: true);
			Directory.Delete(plugins, recursive: true);
		}
	}

	/// <summary>
	/// The build copies the help files next to the server binary (TextFiles/ under bin/), and the default
	/// setting is the relative "TextFiles". Resolved against the working directory it only worked where
	/// the process starts in the binary's folder (the Docker image's /app): `dotnet run --project
	/// SharpMUSH.Server` from the repository root found no help at all, and created an empty
	/// TextFiles/ in the repository.
	/// </summary>
	[Test]
	public async Task ARelativeDirectory_IsTheOneBesideTheServer_NotTheWorkingDirectory()
	{
		await Assert.That(TextFileService.ResolveDirectory("TextFiles", "/srv/sharpmush/bin"))
			.IsEqualTo(Path.Join("/srv/sharpmush/bin", "TextFiles"));
		await Assert.That(TextFileService.ResolveDirectory("/data/help", "/srv/sharpmush/bin"))
			.IsEqualTo("/data/help").Because("an absolute setting is used as given");
	}

	// SharpMUSHOptions is a record with many required properties, so load the checked-in minimal fixture
	// and override only what matters here — the same pattern as SitelockGuardTests.
	private static readonly SharpMUSHOptions BaseConfig =
		ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst");

	/// <summary>
	/// A service rooted at a throwaway directory holding <paramref name="categories"/> categories of one
	/// markdown file each, so a reindex takes long enough for a concurrent reader to land inside it.
	/// </summary>
	private static (TextFileService Service, string Root) BuildServiceOverTempFiles(
		int categories, int entriesPerCategory, PluginCatalog? plugins = null, Lazy<IPluginManager>? manager = null)
	{
		var root = Path.Join(Path.GetTempPath(), $"sharpmush-textfiles-{Guid.NewGuid():N}");
		for (var c = 0; c < categories; c++)
		{
			var dir = Path.Join(root, $"cat{c}");
			Directory.CreateDirectory(dir);
			var body = string.Join("\n", Enumerable.Range(0, entriesPerCategory)
				.Select(e => $"# CAT{c}ENTRY{e}\n\nBody of entry {e} in category {c}.\n"));
			File.WriteAllText(Path.Join(dir, "entries.md"), body);
		}

		var options = BaseConfig with
		{
			TextFile = BaseConfig.TextFile with { TextFilesDirectory = root, CacheOnStartup = false }
		};

		return (new TextFileService(Options.Create(options), NullLogger<TextFileService>.Instance, plugins, manager), root);
	}

	/// <remarks>
	/// Regression: <c>ReindexAsync</c> used to clear the live index and refill it category by category, so
	/// for the duration of the file reads every concurrent <c>help</c> answered from a half-built index —
	/// usually an empty one. It surfaced as a rare unrelated test failure (<c>HelpCommandWorks</c> racing
	/// <c>@readcache</c>), which is how a production symptom looks when nothing asserts on it: on a live
	/// game, <c>@readcache</c> makes help transiently vanish for everyone.
	/// </remarks>
	[Test]
	public async Task ReindexAsync_NeverExposesAPartiallyBuiltIndex()
	{
		var (service, root) = BuildServiceOverTempFiles(categories: 8, entriesPerCategory: 40);
		try
		{
			await service.ReindexAsync();
			var expected = (await service.ListEntriesAsync(string.Empty)).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
			await Assert.That(expected).IsEqualTo(8 * 40);

			using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			var reindexing = Task.Run(async () =>
			{
				while (!stop.IsCancellationRequested) await service.ReindexAsync();
			});

			var shortest = int.MaxValue;
			for (var i = 0; i < 400 && !stop.IsCancellationRequested; i++)
			{
				var seen = (await service.ListEntriesAsync(string.Empty)).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
				shortest = Math.Min(shortest, seen);
			}

			await stop.CancelAsync();
			await reindexing;

			await Assert.That(shortest)
				.IsEqualTo(expected)
				.Because("a reader concurrent with a reindex must see the old index or the new one, never "
					+ "a prefix of the new one");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>
	/// A file reference is a category and a file name joined under the text-files directory. Neither may
	/// climb out of it with <c>..</c>, nor replace it by being rooted: the file sits beside the directory,
	/// readable to the process, and must stay unreachable.
	/// </summary>
	[Test]
	public async Task AReferenceOutsideTheTextFilesDirectory_IsNotFound()
	{
		var (service, root) = BuildServiceOverTempFiles(categories: 1, entriesPerCategory: 1);
		var secretName = $"sharpmush-outside-{Guid.NewGuid():N}.txt";
		var secret = Path.Join(Path.GetDirectoryName(root)!, secretName);
		await File.WriteAllTextAsync(secret, "outside the text files");
		try
		{
			await Assert.That(await service.GetFileContentAsync("cat0/entries.md")).IsNotNull()
				.Because("a name inside the directory still resolves");

			await Assert.That(await service.GetFileContentAsync($"../{secretName}")).IsNull();
			await Assert.That(await service.GetFileContentAsync($"cat0/../../{secretName}")).IsNull();
			await Assert.That(await service.GetFileContentAsync($"cat0/{secret}")).IsNull()
				.Because("a rooted file name would replace the directory outright");
			await Assert.That(await service.ListFilesAsync("..")).DoesNotContain(secretName);
			await Assert.That(await service.ListFilesAsync(Path.GetDirectoryName(root)!)).DoesNotContain(secretName);
		}
		finally
		{
			File.Delete(secret);
			Directory.Delete(root, recursive: true);
		}
	}

	[Test]
	public async Task StripConsecutiveHeaders_SingleHeader_ReturnsUnchanged()
	{
		var content = "# FUNCTION LIST\n  Several major variants of functions are available.";
		var result = TextFileService.StripConsecutiveHeaders(content);
		await Assert.That(result).IsEqualTo(content);
	}

	[Test]
	public async Task StripConsecutiveHeaders_TwoConsecutiveHeaders_KeepsOnlyFirst()
	{
		var content = "# FUNCTION LIST\n# FUNCTION TYPES\n  Several major variants of functions are available.";
		var result = TextFileService.StripConsecutiveHeaders(content);
		await Assert.That(result).IsEqualTo("# FUNCTION LIST\n  Several major variants of functions are available.");
	}

	[Test]
	public async Task StripConsecutiveHeaders_ThreeConsecutiveHeaders_KeepsOnlyFirst()
	{
		var content = "# TOPIC1\n# TOPIC2\n# TOPIC3\n  Body text here.";
		var result = TextFileService.StripConsecutiveHeaders(content);
		await Assert.That(result).IsEqualTo("# TOPIC1\n  Body text here.");
	}

	[Test]
	public async Task StripConsecutiveHeaders_NoHeaders_ReturnsUnchanged()
	{
		var content = "Just some plain text\nwith multiple lines.";
		var result = TextFileService.StripConsecutiveHeaders(content);
		await Assert.That(result).IsEqualTo(content);
	}

	[Test]
	public async Task StripConsecutiveHeaders_HeadersOnly_KeepsFirstAndTheLineBreak()
	{
		var result = TextFileService.StripConsecutiveHeaders("# TOPIC1\n# TOPIC2");
		await Assert.That(result).IsEqualTo("# TOPIC1\n");
	}

	[Test]
	public async Task StripConsecutiveHeaders_CarriageReturns_StayOnTheirLines()
	{
		var result = TextFileService.StripConsecutiveHeaders("# TOPIC1\r\n# TOPIC2\r\n  Body.\r\n");
		await Assert.That(result).IsEqualTo("# TOPIC1\r\n  Body.\r\n");
	}
}
