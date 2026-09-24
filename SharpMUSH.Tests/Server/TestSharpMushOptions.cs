using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// A fully-populated <see cref="SharpMUSHOptions"/> for controller and service tests, built from
/// <see cref="SharpMUSHOptions.Default"/> so a harness cannot describe a game nobody runs. It used to
/// restate every option, and had drifted onto the wrong side of six of them.
/// </summary>
internal static class TestSharpMushOptions
{
	public static SharpMUSHOptions Create(
		bool allowBrowserCode = false,
		string wikiDefaultLocale = WikiOptions.DefaultLocaleFallback)
	{
		var options = SharpMUSHOptions.Default();

		return options with
		{
			// No world is created behind these tests, so the seeded ancestors and handlers do not
			// exist; a test that needs one says so (HttpHandlerSitePolicyTests sets HttpHandler).
			Database = options.Database with
			{
				AncestorExit = null,
				AncestorPlayer = null,
				AncestorRoom = null,
				AncestorThing = null,
				EventHandler = null,
				HttpHandler = null,
				PackageManager = null,
				AllowBrowserCode = allowBrowserCode
			},
			// One banned name and no sitelock rules: the examples the default seeds would make every
			// name and site assertion depend on which example happened to be listed.
			BannedNames = new BannedNamesOptions(BannedNames: ["Guest"]),
			SitelockRules = new SitelockRulesOptions(Rules: new Dictionary<string, string[]>()),
			Alias = new AliasOptions(
				FunctionAliases: new Dictionary<string, string[]>(),
				CommandAliases: new Dictionary<string, string[]>()),
			Wiki = new WikiOptions(DefaultLocale: wikiDefaultLocale)
		};
	}

	/// <summary>A minimal <see cref="IOptionsWrapper{T}"/> returning a fixed options snapshot.</summary>
	public sealed class FixedWrapper(SharpMUSHOptions value) : IOptionsWrapper<SharpMUSHOptions>
	{
		public SharpMUSHOptions CurrentValue { get; } = value;
	}
}
