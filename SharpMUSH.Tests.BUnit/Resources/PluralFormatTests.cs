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

	[Test]
	public async Task EveryConvertedValueParsesInBothLanguages()
	{
		// A malformed ICU pattern throws only when rendered, so a typo in one locale's resx would
		// otherwise surface as an exception in front of a reader rather than here.
		var keys = new[]
		{
			"ConfigSavedCount", "ErrorCount", "PkgConflictCount", "PkgOccurrences",
			"PkgUninstallConfirmBody", "ProfileUpdated", "RolEditedTimes", "RolPoseCount",
			"WarningCount", "WidScenePoseCount", "WikiBatchFailed", "WikiBatchSucceeded",
			"WikiDeleteConfirmText", "ResChangeCount"
		};

		var failures = new List<string>();
		foreach (var tag in new[] { "en", "fr" })
		{
			using var culture = CultureScope.For(tag);
			var loc = PortalLocalizer.Create();
			foreach (var key in keys)
			{
				try
				{
					// Every converted value takes a "count" argument; the two-count one also takes
					// "attributes", and PkgOccurrences a non-count "location". Supplying a superset is
					// harmless — MessageFormat ignores arguments a pattern does not reference.
					var rendered = loc.Plural(key, new Dictionary<string, object?>
					{
						["count"] = 2,
						["objects"] = 2,
						["attributes"] = 2,
						["location"] = "somewhere"
					});
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
}
