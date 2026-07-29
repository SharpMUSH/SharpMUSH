using System.Globalization;
using System.Resources;
using SharpMUSH.Client.Resources;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// Makes "the portal supports German" a claim a test can fail.
/// <para>
/// Every locale <see cref="PortalLocales.Codes"/> offers must ship a satellite assembly that
/// carries its own value for every <em>player-facing</em> key. Staff surfaces are two thirds of the
/// strings and are allowed to lag — that is the runbook's position
/// (<c>docs/localization/adding-languages.md</c> step 5) and this test encodes exactly that much.
/// </para>
/// <para>
/// The cases come from <see cref="PortalLocales.Codes"/> rather than a hand-written
/// <c>[Arguments]</c> list, so declaring a locale in the picker without translating it fails here
/// instead of silently shipping an English UI under a German menu entry.
/// </para>
/// </summary>
public class DeclaredLocaleCoverageTests
{
	public static IEnumerable<string> TranslatedLocales() =>
		PortalLocales.Codes.Where(c => !string.Equals(c, "en", StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// The keys a player sees. Derived from the same prefix map the translation tooling batches by,
	/// so the gate and the extraction tool cannot disagree about what "player-facing" means.
	/// </summary>
	private static IReadOnlyList<string> PlayerFacingKeys() =>
		NeutralKeys().Where(PortalSurfaces.IsPlayerFacing).Order(StringComparer.Ordinal).ToList();

	private static IEnumerable<string> NeutralKeys()
	{
		var set = SharedResources.Manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)
			?? throw new InvalidOperationException("the neutral SharedResource set is missing from SharpMUSH.Client");

		return set.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key);
	}

	[Test]
	public async Task The_player_facing_key_set_is_a_meaningful_slice_of_the_resx()
	{
		// Guards the gate itself: a prefix map that stopped matching anything would leave every
		// per-locale assertion below trivially satisfied, and nothing else would notice.
		var all = NeutralKeys().Count();
		var playerFacing = PlayerFacingKeys().Count;

		await Assert.That(playerFacing).IsGreaterThan(200);
		await Assert.That(playerFacing).IsLessThan(all / 2)
			.Because("staff strings are the bulk of the resx; if this flips, the split has been mis-derived");
	}

	[Test]
	[MethodDataSource(nameof(TranslatedLocales))]
	public async Task A_declared_locale_ships_a_satellite_assembly(string tag)
	{
		var own = SharedResources.OwnSet(tag);

		await Assert.That(own).IsNotNull()
			.Because($"{tag} is offered by the language picker but has no SharedResource.{tag}.resx");
	}

	[Test]
	[MethodDataSource(nameof(TranslatedLocales))]
	public async Task A_declared_locale_translates_every_player_facing_key(string tag)
	{
		// tryParents: false is the whole point. IStringLocalizer's ResourceNotFound is false for a key
		// the locale does not have, because ResourceManager falls back to the neutral resource and
		// hands back the English string — so an assertion on ResourceNotFound passes for a locale that
		// renders entirely in English. Only the locale's own resource set answers the question asked.
		var own = SharedResources.OwnSet(tag)
			?? throw new InvalidOperationException($"no satellite for {tag}");

		var missing = PlayerFacingKeys().Where(k => own.GetString(k) is null).ToList();

		await Assert.That(missing).IsEmpty()
			.Because($"{tag} would fall back to English for these player-facing keys");
	}

	[Test]
	[MethodDataSource(nameof(TranslatedLocales))]
	public async Task The_production_localizer_serves_the_satellite_value_under_that_culture(string tag)
	{
		// The test above reads the satellite directly; this one proves the registered
		// IStringLocalizer<SharedResource> actually reaches it, which is the path the portal uses and
		// the path that broke once already (ResourcesPath double-rooting the manifest name).
		var own = SharedResources.OwnSet(tag)
			?? throw new InvalidOperationException($"no satellite for {tag}");

		using var culture = CultureScope.For(tag);
		var loc = PortalLocalizer.Create();

		var wrong = PlayerFacingKeys()
			.Where(k => loc[k].Value != own.GetString(k))
			.ToList();

		await Assert.That(wrong).IsEmpty();
	}

	[Test]
	[MethodDataSource(nameof(TranslatedLocales))]
	public async Task A_declared_locale_never_invents_a_key_the_neutral_resx_lacks(string tag)
	{
		var own = SharedResources.OwnSet(tag)
			?? throw new InvalidOperationException($"no satellite for {tag}");

		var neutral = NeutralKeys().ToHashSet(StringComparer.Ordinal);
		var stray = own.Cast<System.Collections.DictionaryEntry>()
			.Select(e => (string)e.Key)
			.Where(k => !neutral.Contains(k))
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(stray).IsEmpty()
			.Because("a key with no English original is dead weight no code path can reach");
	}
}

internal static class SharedResources
{
	public static readonly ResourceManager Manager =
		new("SharpMUSH.Client.Resources.SharedResource", typeof(SharedResource).Assembly);

	/// <summary>
	/// A locale's <em>own</em> resource set — no parent fallback — or <c>null</c> when it ships no
	/// satellite assembly at all.
	/// </summary>
	public static ResourceSet? OwnSet(string tag) =>
		Manager.GetResourceSet(new CultureInfo(tag), createIfNotExists: true, tryParents: false);
}
