using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Documentation;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Pins the glob dialect of each site that matches a user- or admin-written wildcard outside softcode:
/// help topics, text-file entries, sitelock rules and attribute enums. Each uses the general MUSH
/// wildcard (<see cref="SharpMUSH.Library.Markup.MushText.Glob"/>): <c>*</c> any run, <c>?</c> one
/// character, <c>\</c> making the next character literal. They each used to hand-roll a translation in
/// which <c>\</c> was an ordinary character, so <c>\*</c> demanded a backslash followed by anything.
/// <para>
/// This is deliberately not the attribute-name dialect, where a single <c>*</c> stays inside one
/// backtick level; <c>AttributeGlobDialectTests</c> pins that one.
/// </para>
/// </summary>
public class GlobDialectSiteTests
{
	private static readonly SharpMUSHOptions BaseConfig =
		ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst");

	// ---- Help topics (SharpMUSH.Documentation.Helpfiles) --------------------------------------------

	[Test]
	[Arguments("@*", new[] { "@create", "@dig" })]
	[Arguments("@d?g", new[] { "@dig" })]
	[Arguments("@DIG", new[] { "@dig" })]
	[Arguments(@"star\*", new[] { "star*" })]
	[Arguments(@"star\?", new string[0])]
	public async Task HelpTopics_UseTheGeneralWildcard(string pattern, string[] expected)
	{
		var dir = Directory.CreateTempSubdirectory("sharpmush-help-glob-");
		try
		{
			await File.WriteAllTextAsync(Path.Join(dir.FullName, "topics.md"),
				"# @create\nCreate.\n\n# @dig\nDig.\n\n# star*\nLiteral star.\n\n# starx\nNot a star.\n");
			var help = new Helpfiles(dir);
			help.Index();

			await Assert.That(help.FindMatchingTopics(pattern).Order(StringComparer.Ordinal).ToArray())
				.IsEquivalentTo(expected);
		}
		finally
		{
			dir.Delete(recursive: true);
		}
	}

	// ---- Text-file entries (TextFileService.SearchEntriesAsync) ------------------------------------

	[Test]
	[Arguments("ENTRY*", new[] { "ENTRYONE", "ENTRYTWO" })]
	[Arguments("ENTRY?NE", new[] { "ENTRYONE" })]
	[Arguments("entry*", new[] { "ENTRYONE", "ENTRYTWO" })]
	[Arguments(@"STAR\*", new[] { "STAR*" })]
	public async Task TextFileEntries_UseTheGeneralWildcard(string pattern, string[] expected)
	{
		var root = Directory.CreateTempSubdirectory("sharpmush-textfile-glob-");
		try
		{
			var category = Directory.CreateDirectory(Path.Join(root.FullName, "cat"));
			await File.WriteAllTextAsync(Path.Join(category.FullName, "entries.md"),
				"# ENTRYONE\n\nOne.\n\n# ENTRYTWO\n\nTwo.\n\n# STAR*\n\nLiteral star.\n\n# STARX\n\nNot a star.\n");
			var options = BaseConfig with
			{
				TextFile = BaseConfig.TextFile with { TextFilesDirectory = root.FullName, CacheOnStartup = false }
			};
			var service = new TextFileService(Options.Create(options), NullLogger<TextFileService>.Instance);
			await service.ReindexAsync();

			var found = (await service.SearchEntriesAsync(string.Empty, pattern))
				.Select(entry => entry.ToUpperInvariant())
				.Order(StringComparer.Ordinal)
				.ToArray();

			await Assert.That(found).IsEquivalentTo(expected);
		}
		finally
		{
			root.Delete(recursive: true);
		}
	}

	// ---- Sitelock rules (SitelockMatcher) — decides who is banned -----------------------------------

	[Test]
	[Arguments("*.evil.com", "x.evil.com")]
	[Arguments("*.EVIL.com", "x.evil.com")]
	[Arguments("host?.evil.com", "host7.evil.com")]
	[Arguments(@"odd\*name.example", "odd*name.example")]
	public async Task Sitelock_GlobMatchesHost(string rule, string host)
		=> await Assert.That(SitelockMatcher.Matches(rule, "192.0.2.1", host)).IsTrue();

	[Test]
	[Arguments("*.evil.com", "evil.com.good.org")]
	[Arguments("host?.evil.com", "host77.evil.com")]
	[Arguments(@"odd\*name.example", "oddXname.example")]
	[Arguments(@"odd\*name.example", @"odd\Xname.example")]
	public async Task Sitelock_GlobRefusesHost(string rule, string host)
		=> await Assert.That(SitelockMatcher.Matches(rule, "192.0.2.1", host)).IsFalse();

	[Test]
	[Arguments("10.0.0.?", "10.0.0.7", true)]
	[Arguments("10.0.0.?", "10.0.0.77", false)]
	[Arguments("10.*", "10.200.3.4", true)]
	[Arguments("10.*", "110.200.3.4", false)]
	public async Task Sitelock_GlobOnTheAddressString(string rule, string ip, bool expected)
		=> await Assert.That(SitelockMatcher.Matches(rule, ip, "unresolved.example")).IsEqualTo(expected);

	// ---- Attribute enums (ValidateService) -----------------------------------------------------------

	/// <remarks>
	/// Case-sensitive, unlike every other site here. PennMUSH's <c>@attribute/enum</c> is a different
	/// contract altogether (a case-insensitive prefix list, <c>src/atr_tab.c</c>); globbing enum
	/// entries is SharpMUSH's own, and these pin only which wildcard it speaks.
	/// </remarks>
	[Test]
	[Arguments("reddish", true)]
	[Arguments("blue", true)]
	[Arguments("Blue", false)]
	[Arguments("green", false)]
	[Arguments("star*", true)]
	[Arguments("stars", false)]
	[Arguments(@"star\s", false)]
	[Arguments("plain", true)]
	public async Task AttributeEnum_UsesTheGeneralWildcardCaseSensitively(string value, bool expected)
	{
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(BaseConfig);
		var service = new ValidateService(Substitute.For<IMediator>(), options, Substitute.For<ILockService>());
		var entry = new SharpAttributeEntry
		{
			Name = "COLOUR",
			DefaultFlags = [],
			Enum = ["red*", "bl?e", @"star\*", "plain"]
		};

		var valid = await service.Valid(IValidateService.ValidationType.AttributeValue, MarkupText.Plain(value), entry);

		await Assert.That(valid).IsEqualTo(expected);
	}
}
