using System.Text.RegularExpressions;
using SharpMUSH.Client.Resources;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// Keeps the two places that describe the portal's locale surface honest against each other. Both
/// files are copied into the test output by <c>SharpMUSH.Tests.BUnit.csproj</c> so they can be read
/// here without guessing at a repository root.
/// </summary>
public class PortalSurfacesTests
{
	private static readonly Regex SurfaceEntry = new("""^\s*"(?<prefix>\w+)"\s*:\s*"(?<surface>[^"]*)"\s*,\s*$""",
		RegexOptions.Multiline);

	private static string ToolingSource() =>
		File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "i18n", "extract_untranslated.py"));

	private static string ClientProject() =>
		File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "i18n", "SharpMUSH.Client.csproj"));

	[Test]
	public async Task The_surface_map_matches_the_extraction_tool()
	{
		// PortalSurfaces is a hand-kept mirror of the Python SURFACES dict. Without this, adding a key
		// prefix to the tooling would quietly shrink the set of keys the per-locale gate covers.
		var body = ToolingSource();
		var start = body.IndexOf("SURFACES = {", StringComparison.Ordinal);
		var end = body.IndexOf("\n}", start, StringComparison.Ordinal);

		await Assert.That(start).IsGreaterThan(-1);
		await Assert.That(end).IsGreaterThan(start);

		var fromTooling = SurfaceEntry.Matches(body[start..end])
			.ToDictionary(m => m.Groups["prefix"].Value, m => m.Groups["surface"].Value);

		await Assert.That(fromTooling).IsEquivalentTo(PortalSurfaces.ByPrefix);
	}

	[Test]
	public async Task The_default_surface_matches_the_extraction_tool()
	{
		await Assert.That(ToolingSource()).Contains($"return \"{PortalSurfaces.Default}\"");
	}

	[Test]
	public async Task WidgetZone_keys_are_staff_strings_despite_the_player_facing_Wid_prefix()
	{
		await Assert.That(PortalSurfaces.IsPlayerFacing("WidChatTitle")).IsTrue();
		await Assert.That(PortalSurfaces.IsPlayerFacing("WidgetZoneMainContent")).IsFalse();
	}

	[Test]
	public async Task An_unprefixed_key_falls_back_to_the_configuration_surface()
	{
		await Assert.That(PortalSurfaces.SurfaceOf("MudTheme")).IsEqualTo(PortalSurfaces.Default);
		await Assert.That(PortalSurfaces.IsPlayerFacing("MudTheme")).IsFalse();
	}

	[Test]
	public async Task SatelliteResourceLanguages_lists_every_declared_locale()
	{
		// PublishTrimmed can drop satellite assemblies the app only reaches by runtime culture lookup,
		// so a locale missing from this property ships a picker entry that renders English — and only
		// in a published build, which no Debug-configuration test would ever catch.
		var match = Regex.Match(ClientProject(), "<SatelliteResourceLanguages>(?<value>[^<]*)</SatelliteResourceLanguages>");

		await Assert.That(match.Success).IsTrue()
			.Because("SharpMUSH.Client.csproj must pin the satellite languages");

		var declared = match.Groups["value"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		await Assert.That(declared).IsEquivalentTo(PortalLocales.Codes.ToArray());
	}

	[Test]
	public async Task The_WASM_build_loads_full_ICU_globalization_data()
	{
		// Nothing observable from this suite: it runs on the desktop runtime, which always has full ICU,
		// so every CultureInfo assertion here passes whatever the browser bundle ships. Only the browser
		// is sharded, and only one shard can be selected — the portal's locales span all three partial
		// sets. Reading the property is the sole way this suite can hold the line, and the failure it
		// guards against is silent: a locale outside the shard renders fully translated and still
		// formats its dates in English.
		await Assert.That(ClientProject())
			.Contains("<BlazorWebAssemblyLoadAllGlobalizationData>true</BlazorWebAssemblyLoadAllGlobalizationData>");
	}
}
