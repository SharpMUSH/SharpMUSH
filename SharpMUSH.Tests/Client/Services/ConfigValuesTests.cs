using System.Text.Json;
using SharpMUSH.Client.Models.Configuration;
using PropertyMetadata = SharpMUSH.Library.API.PropertyMetadata;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// The value engine behind <c>/admin/config/{category}</c>. It used to live inside
/// <c>DynamicConfig.razor</c>'s 691-line <c>@code</c> block, where nothing could reach it: these are
/// pure functions over a value map, and the cases below — a <c>uint</c> where the editor wants an
/// <c>int</c>, a <c>JsonElement</c> straight off the wire, a half-typed number — are exactly what the
/// server's own options hand it.
/// </summary>
public class ConfigValuesTests
{
	private static PropertyMetadata Property(
		string type, object? defaultValue = null, bool required = false, string path = "Net.Port") =>
		new() { Name = "Port", Path = path, Type = type, DefaultValue = defaultValue, Required = required };

	private static Dictionary<string, object?> Values(object? value, string path = "Net.Port") =>
		new() { [path] = value };

	private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

	[Test]
	[Arguments(4201, 4201)]
	[Arguments((uint)4201, 4201)]
	[Arguments(4201L, 4201)]
	public async Task NullableIntAcceptsTheWidthsTheOptionsActuallyUse(object stored, int expected)
	{
		await Assert.That(ConfigValues.NullableInt(Values(stored), Property("integer"))).IsEqualTo(expected);
	}

	[Test]
	public async Task NullableIntReadsANumberStraightOffTheWire()
	{
		await Assert.That(ConfigValues.NullableInt(Values(Json("4201")), Property("integer"))).IsEqualTo(4201);
	}

	/// <summary>
	/// An explicit null is "no value", which is different from an absent key. The absent key takes
	/// the property default, and zero when there is not one — the editor's field has to show
	/// something.
	/// </summary>
	[Test]
	public async Task NullableIntDistinguishesAnExplicitNullFromAnAbsentKey()
	{
		await Assert.That(ConfigValues.NullableInt(Values(null), Property("integer", defaultValue: 99))).IsNull();
		await Assert.That(ConfigValues.NullableInt(new Dictionary<string, object?>(), Property("integer", defaultValue: 99))).IsEqualTo(99);
		await Assert.That(ConfigValues.NullableInt(new Dictionary<string, object?>(), Property("integer"))).IsEqualTo(0);
	}

	[Test]
	public async Task BoolFallsBackToTheDefaultAndThenToFalse()
	{
		await Assert.That(ConfigValues.Bool(Values(true), Property("boolean"))).IsTrue();
		await Assert.That(ConfigValues.Bool(new Dictionary<string, object?>(), Property("boolean", defaultValue: true))).IsTrue();
		await Assert.That(ConfigValues.Bool(new Dictionary<string, object?>(), Property("boolean"))).IsFalse();
		await Assert.That(ConfigValues.Bool(Values("yes"), Property("boolean"))).IsFalse()
			.Because("a value of the wrong type is not a truthy one");
	}

	/// <summary>
	/// The displayed string is parsed back by <see cref="ConfigValues.ParseNumeric"/>. A locale that
	/// writes <c>1,5</c> would round-trip a double into a different number or into nothing, so both
	/// ends are pinned to the invariant culture.
	/// </summary>
	[Test]
	public async Task ADoubleRoundTripsThroughItsDisplayedString()
	{
		var property = Property("number");

		var shown = ConfigValues.NumericString(Values(1.5), property);
		await Assert.That(shown).IsEqualTo("1.5");

		await Assert.That(ConfigValues.ParseNumeric(property, shown, out var parsed)).IsTrue();
		await Assert.That(parsed).IsEqualTo(1.5);
	}

	/// <summary>
	/// Text that is not a number yet must leave the stored value alone. Blanking the field under the
	/// operator's cursor the moment they type a lone minus sign is the failure this prevents.
	/// </summary>
	[Test]
	[Arguments("-")]
	[Arguments("1e")]
	[Arguments("abc")]
	public async Task ParseNumericRefusesAHalfTypedNumber(string raw)
	{
		await Assert.That(ConfigValues.ParseNumeric(Property("integer"), raw, out _)).IsFalse();
	}

	/// <summary>
	/// Cleared text is a real edit, not an unparseable one: null for an optional property, and zero
	/// for a required one, which has nowhere to put "no value".
	/// </summary>
	/// <remarks>
	/// The required-integer zero must be boxed as an <see cref="int"/>. The inlined version of this
	/// wrote one ternary whose common type was <see cref="double"/>, so clearing a required integer
	/// field stored <c>0.0</c>; <c>Equals(0.0, 0)</c> is false, so the field counted as changed
	/// against an unchanged original and the save sent a double for an integer option.
	/// </remarks>
	[Test]
	public async Task ClearingTheFieldIsNullWhenOptionalAndZeroWhenRequired()
	{
		await Assert.That(ConfigValues.ParseNumeric(Property("integer"), "  ", out var optional)).IsTrue();
		await Assert.That(optional).IsNull();

		await Assert.That(ConfigValues.ParseNumeric(Property("integer", required: true), "", out var integer)).IsTrue();
		await Assert.That(integer).IsTypeOf<int>();
		await Assert.That(integer).IsEqualTo(0);

		await Assert.That(ConfigValues.ParseNumeric(Property("number", required: true), "", out var number)).IsTrue();
		await Assert.That(number).IsTypeOf<double>();
		await Assert.That(number).IsEqualTo(0.0);
	}

	[Test]
	public async Task CoerceToStringListHandlesEveryShapeAValueArrivesIn()
	{
		await Assert.That(ConfigValues.CoerceToStringList(new[] { "a", "b" })).IsEquivalentTo(new[] { "a", "b" });
		await Assert.That(ConfigValues.CoerceToStringList(new List<string> { "a" })).IsEquivalentTo(new[] { "a" });
		await Assert.That(ConfigValues.CoerceToStringList(Json("""["a","b"]"""))).IsEquivalentTo(new[] { "a", "b" });
		await Assert.That(ConfigValues.CoerceToStringList(new[] { 1, 2 })).IsEquivalentTo(new[] { "1", "2" });
		await Assert.That(ConfigValues.CoerceToStringList(42)).IsEmpty()
			.Because("an unrecognised shape is an empty list, never null");
		await Assert.That(ConfigValues.CoerceToStringList("abc")).IsEmpty()
			.Because("a string is not a list of its characters");
	}

	[Test]
	public async Task StringListDropsBlankEntries()
	{
		var values = Values(new[] { "a", "  ", "", "b" });

		await Assert.That(ConfigValues.StringList(values, Property("array"))).IsEquivalentTo(new[] { "a", "b" });
	}

	[Test]
	public async Task DictionaryEntriesComeBackFromEveryShapeAndCommitToOne()
	{
		var fromJson = ConfigValues.BuildDictEntries(Json("""{"wizard":["a",""],"royalty":[]}"""));
		await Assert.That(fromJson.Select(e => e.Key)).IsEquivalentTo(new[] { "wizard", "royalty" });
		await Assert.That(fromJson[0].Values).IsEquivalentTo(new[] { "a" });

		var fromClr = ConfigValues.BuildDictEntries(
			new Dictionary<string, string[]> { ["wizard"] = ["a", " "] });
		await Assert.That(fromClr[0].Values).IsEquivalentTo(new[] { "a" });

		await Assert.That(ConfigValues.BuildDictEntries(null)).IsEmpty();
		await Assert.That(ConfigValues.BuildDictEntries("not a dictionary")).IsEmpty();
	}

	/// <summary>
	/// A row whose key has not been typed yet is not a setting. A repeated key keeps the last row,
	/// which is the one the operator edited most recently.
	/// </summary>
	[Test]
	public async Task CommittingRowsDropsBlankKeysAndKeepsTheLastOfARepeat()
	{
		var committed = ConfigValues.ToDictionary(
		[
			new ConfigDictEntry { Key = "wizard", Values = ["first"] },
			new ConfigDictEntry { Key = "  ", Values = ["orphan"] },
			new ConfigDictEntry { Key = "wizard", Values = ["second"] },
		]);

		await Assert.That(committed.Keys).IsEquivalentTo(new[] { "wizard" });
		await Assert.That(committed["wizard"]).IsEquivalentTo(new[] { "second" });
	}

	/// <summary>
	/// The changed set drives both the "N unsaved changes" counter and the save request, so the two
	/// cannot disagree about which properties changed.
	/// </summary>
	[Test]
	public async Task ChangesAreTheValuesThatDifferFromTheOnesLoaded()
	{
		var original = new Dictionary<string, object?> { ["a"] = 1, ["b"] = "x", ["c"] = null };
		var current = new Dictionary<string, object?> { ["a"] = 1, ["b"] = "y", ["c"] = 0 };

		var changes = ConfigValues.Changes(current, original);

		await Assert.That(changes.Keys).IsEquivalentTo(new[] { "b", "c" });
		await Assert.That(changes["b"]).IsEqualTo("y");
	}

	[Test]
	public async Task IntBoundReadsABoundFromEveryShapeItArrivesIn()
	{
		await Assert.That(ConfigValues.IntBound(5)).IsEqualTo(5);
		await Assert.That(ConfigValues.IntBound((uint)5)).IsEqualTo(5);
		await Assert.That(ConfigValues.IntBound(Json("5"))).IsEqualTo(5);
		await Assert.That(ConfigValues.IntBound(null)).IsNull();
		await Assert.That(ConfigValues.IntBound("5")).IsNull();
	}
}
