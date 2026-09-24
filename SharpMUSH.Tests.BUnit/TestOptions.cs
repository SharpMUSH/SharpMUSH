using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Tests.BUnit;

/// <summary>
/// A fully-populated <see cref="SharpMUSHOptions"/> for the controller fixtures, built from
/// <see cref="SharpMUSHOptions.Default"/> — the one place a shipped default is written down.
/// </summary>
/// <remarks>
/// Two fixtures each restated all 194 options by hand, and both had drifted from the default on ten
/// of them (<c>MaxDepth</c>, <c>PlayerNameLen</c>, <c>FloatPrecision</c>, <c>EmptyAttributes</c>,
/// <c>ExitsConnectRooms</c>, <c>HttpRequestsPerSecond</c>, <c>Pueblo</c>, <c>ColorsFile</c>,
/// <c>ErrorLog</c>, <c>QueueEntryCpuTime</c>). No test asserted any of them, so the copies were
/// describing a game nobody runs. This project cannot share <c>SharpMUSH.Tests</c>'
/// <c>TestSharpMushOptions</c>: that lives behind <c>SharpMUSH.Tests.Infrastructure</c>, which drags
/// in the database providers and Testcontainers that these tests exist to stay clear of.
/// </remarks>
internal static class TestOptions
{
	public static SharpMUSHOptions Create(bool allowBrowserCode = false)
	{
		var options = SharpMUSHOptions.Default();

		return options with
		{
			// No world is created behind these tests, so the seeded ancestors and handlers do not exist.
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
			// One banned name and no sitelock rules, so a name or site assertion does not depend on which
			// of the default's examples happened to be listed.
			BannedNames = new BannedNamesOptions(BannedNames: ["Guest"]),
			SitelockRules = new SitelockRulesOptions(Rules: new Dictionary<string, string[]>()),
			Alias = new AliasOptions(
				FunctionAliases: new Dictionary<string, string[]>(),
				CommandAliases: new Dictionary<string, string[]>())
		};
	}
}
