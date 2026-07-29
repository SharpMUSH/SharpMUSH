using System.Text.RegularExpressions;
using SharpMUSH.Client.Resources;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// Proves the plural mechanism actually selects per-locale categories. Without these, a three-category
/// resource value silently rendering one form looks identical to success.
/// </summary>
public class PluralFormatTests
{
	/// <summary>The wording of a rendered value with the number removed, so comparisons see the form only.</summary>
	private static string WordsOnly(string rendered) => Regex.Replace(rendered, @"\d+", "").Trim();

	[Test]
	public async Task Russian_selectsAllThreeCategoriesWhenThePatternSuppliesThem()
	{
		// The load-bearing assertion: that CLDR's Russian rules actually drive category selection.
		// Russian keys on the last digits — 1 -> one, 2-4 -> few, 0 and 5-20 -> many.
		//
		// Deliberately an inline pattern rather than a resx lookup. There is no ru satellite yet, so
		// going through the localizer would fetch the *English* two-category value and apply Russian
		// rules to it; `few` would fall through to `other` and this could never observe three forms.
		// What needs proving here is the mechanism, and the mechanism is culture -> category selection.
		//
		// Comparing WordsOnly, not the raw strings: the number differs in every case, so raw
		// comparison finds three "distinct" results even for `{count} pose(s)` — which is exactly how
		// the first version of this test passed while proving nothing.
		using var culture = CultureScope.For("ru");
		const string pattern = "{count, plural, one {# поза} few {# позы} many {# поз} other {# поз}}";

		var one = WordsOnly(PluralFormat.Render(pattern, "count", 1));
		var few = WordsOnly(PluralFormat.Render(pattern, "count", 2));
		var many = WordsOnly(PluralFormat.Render(pattern, "count", 5));

		await Assert.That(one).IsEqualTo("поза");
		await Assert.That(few).IsEqualTo("позы");
		await Assert.That(many).IsEqualTo("поз");
		await Assert.That(new[] { one, few, many }.Distinct().Count())
			.IsEqualTo(3)
			.Because($"Russian needs three forms; got '{one}', '{few}', '{many}'");
	}

	[Test]
	public async Task TheRussianSatelliteItselfSuppliesThreeForms()
	{
		// The test above pins the mechanism with an inline pattern, which was all that could be proved
		// when no `ru` satellite existed. Now that one ships, this asserts the thing that actually
		// reaches a reader: that the *resource* carries three categories. A resx that quietly lost its
		// `many` branch would still pass the inline test and still render Russian — just with the wrong
		// form for every count from 5 up.
		using var culture = CultureScope.For("ru");
		var loc = PortalLocalizer.Create();

		var one = WordsOnly(loc.Plural("RolPoseCount", "count", 1));
		var few = WordsOnly(loc.Plural("RolPoseCount", "count", 2));
		var many = WordsOnly(loc.Plural("RolPoseCount", "count", 5));

		// WordsOnly, because the number differs in all three: raw strings are always distinct and would
		// make this pass against a single-form value.
		await Assert.That(new[] { one, few, many }.Distinct().Count())
			.IsEqualTo(3)
			.Because($"the ru resource should supply one/few/many; got '{one}', '{few}', '{many}'");

		// If the satellite failed to resolve, every lookup would fall back to the English neutral value
		// and the assertion above would see only two distinct forms — but say so explicitly, because
		// "fell back to English" is the failure this whole locale programme exists to catch.
		await Assert.That(one).IsNotEqualTo("pose").Because("a ru lookup returning English means the satellite did not load");
	}

	[Test]
	public async Task TheSameNumberCanSelectDifferentCategoriesInDifferentLocales()
	{
		// 2 is `few` in Russian and `other` in English. If the culture were not reaching the formatter,
		// both would take the same branch and this would fail — which is the failure mode that a raw
		// string comparison in the test above was blind to.
		const string pattern = "{count, plural, one {ONE} few {FEW} many {MANY} other {OTHER}}";

		string russian, english;
		using (var _ = CultureScope.For("ru")) russian = PluralFormat.Render(pattern, "count", 2);
		using (var _ = CultureScope.For("en")) english = PluralFormat.Render(pattern, "count", 2);

		await Assert.That(russian).IsEqualTo("FEW");
		await Assert.That(english).IsEqualTo("OTHER");
	}

	[Test]
	public async Task English_rendersTwoFormsAndInterpolatesTheNumber()
	{
		using var culture = CultureScope.For("en");
		var loc = PortalLocalizer.Create();

		await Assert.That(loc.Plural("RolPoseCount", "count", 1)).IsEqualTo("1 pose");
		await Assert.That(loc.Plural("RolPoseCount", "count", 4)).IsEqualTo("4 poses");
	}

	[Test]
	public async Task French_usesItsOwnCategoriesRatherThanEnglishs()
	{
		using var culture = CultureScope.For("fr");
		var loc = PortalLocalizer.Create();

		var one = loc.Plural("RolPoseCount", "count", 1);
		var other = loc.Plural("RolPoseCount", "count", 4);

		await Assert.That(one).IsNotEqualTo(other);
		await Assert.That(one).Contains("pose");
		await Assert.That(other).Contains("poses");
	}

	[Test]
	public async Task TwoIndependentCountsInOneSentenceEachSelectTheirOwnForm()
	{
		// PkgUninstallConfirmBody is why key pairs could never have worked: two counts in one sentence
		// would need one key per combination of categories — sixteen of them in Polish.
		using var culture = CultureScope.For("en");
		var loc = PortalLocalizer.Create();

		var singularBoth = loc.Plural("PkgUninstallConfirmBody", "objects", 1, "attributes", 1);
		var mixed = loc.Plural("PkgUninstallConfirmBody", "objects", 1, "attributes", 3);

		await Assert.That(singularBoth).Contains("1 object ");
		await Assert.That(singularBoth).Contains("1 managed attribute record");
		await Assert.That(mixed).Contains("1 object ");
		await Assert.That(mixed).Contains("3 managed attribute records");
	}

	/// <summary>
	/// Every argument any ICU value in the portal references. MessageFormat ignores arguments a pattern
	/// does not use, so one superset renders them all.
	/// </summary>
	private static readonly Dictionary<string, object?> EveryArgument = new()
	{
		["count"] = 2,
		["objects"] = 2,
		["attributes"] = 2,
		["location"] = "somewhere",
		["signed"] = "+2"
	};

	/// <summary>The ICU-bearing keys, read from the neutral resource rather than restated here.</summary>
	private static IEnumerable<string> IcuKeys()
	{
		// Invariant, not "en": English *is* the neutral resource, so there is no SharedResource.en
		// satellite and enumerating under that culture throws MissingManifestResourceException.
		using var culture = CultureScope.For("");
		return PortalLocalizer.Create()
			.GetAllStrings(includeParentCultures: false)
			.Where(s => s.Value.Contains(", plural,", StringComparison.Ordinal))
			.Select(s => s.Name)
			.ToList();
	}

	[Test]
	public async Task EveryConvertedValueParsesInEveryLocale()
	{
		// A malformed ICU pattern throws only when rendered, so a typo in one locale's resx would
		// otherwise surface as an exception in front of a reader rather than here. Both axes are
		// discovered, not listed: a hardcoded key list stops covering a key added after it was written,
		// and a hardcoded locale pair stopped covering thirteen of the sixteen languages the portal ships.
		var keys = IcuKeys().ToList();
		await Assert.That(keys).IsNotEmpty().Because("the neutral resource should carry ICU values");

		var failures = new List<string>();
		foreach (var tag in PortalLocales.Codes)
		{
			using var culture = CultureScope.For(tag);
			var loc = PortalLocalizer.Create();
			foreach (var key in keys)
			{
				try
				{
					var rendered = loc.Plural(key, EveryArgument);
					if (Regex.IsMatch(rendered, @"\{|\}"))
						failures.Add($"{tag}/{key}: unreplaced brace in '{rendered}'");
				}
				catch (Exception ex)
				{
					failures.Add($"{tag}/{key}: {ex.GetType().Name}: {ex.Message}");
				}
			}
		}

		await Assert.That(failures).IsEmpty();
	}

	[Test]
	public async Task ByteDeltaTakesItsCategoryFromTheMagnitudeAndItsDigitsFromTheSignedString()
	{
		// The only two-argument value where the two disagree on purpose: the sign has to survive into
		// the output, but a plural category can only be chosen from a number. Passing the signed string
		// as the count would render "-1 bytes" in English and ask locales for a form CLDR never defines
		// for negatives; passing the magnitude alone would silently drop the minus in the editor's
		// only indication that an edit removed text.
		using var culture = CultureScope.For("en");
		var loc = PortalLocalizer.Create();

		await Assert.That(loc.Plural("WkBytesDelta", "count", 1, "signed", "+1")).IsEqualTo("+1 byte");
		await Assert.That(loc.Plural("WkBytesDelta", "count", 1, "signed", "-1")).IsEqualTo("-1 byte");
		await Assert.That(loc.Plural("WkBytesDelta", "count", 42, "signed", "-42")).IsEqualTo("-42 bytes");
	}

	[Test]
	public async Task TheCharacterDirectoryCountCollapsedToOneKey()
	{
		// Was NavCharacterRegistered/NavCharactersRegistered chosen by `_all.Count == 1` at the call
		// site — English's boundary hardcoded into C#, which no amount of translation could fix for
		// Polish or Russian.
		using var culture = CultureScope.For("en");
		var loc = PortalLocalizer.Create();

		await Assert.That(loc.Plural("NavCharactersRegistered", "count", 1)).IsEqualTo("1 character registered");
		await Assert.That(loc.Plural("NavCharactersRegistered", "count", 7)).IsEqualTo("7 characters registered");
	}
}
