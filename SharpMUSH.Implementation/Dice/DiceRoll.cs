using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Dice;

public enum DiceRollMode
{
	Random,

	/// <summary>Every die shows its lowest face, with no explosions or rerolls: the formula's floor.</summary>
	Minimize,

	/// <summary>Every die shows its highest face, with no explosions or rerolls: the formula's ceiling.</summary>
	Maximize
}

/// <summary>
/// Rolls a dice formula: <c>2d6+5</c>, <c>4d6kh3</c>, <c>1dF+DEXTERITY</c>.
/// </summary>
public static class DiceRoll
{
	/// <summary>Dice rolled by one formula, rerolls and explosions included.</summary>
	public const int MaxDice = 1000;

	public const int MaxSides = 1_000_000;

	/// <summary>How many times one die may explode or be rerolled in a row.</summary>
	public const int MaxRepeats = 100;

	/// <summary>How deep values may refer to other values.</summary>
	public const int MaxVariableDepth = 32;

	/// <summary>Operands in one formula, counting each use of a value's own formula again.</summary>
	public const int MaxTerms = 1000;

	/// <summary>How deep brackets, pools, function calls and values may nest, all counted together.</summary>
	public const int MaxNesting = 64;

	/// <summary>How many different names one roll may look up.</summary>
	public const int MaxNames = 64;

	/// <param name="formula">The formula as typed.</param>
	/// <param name="lookup">What a name holds. Called once per name; see <see cref="DiceValue"/>.</param>
	/// <param name="roll">Rolls one die: given its number of sides, a face from 1 to that number.</param>
	/// <param name="mode">Roll, or answer the lowest or highest the formula can come to.</param>
	public static async ValueTask<Result<DiceRollResult>> RollAsync(string formula,
		Func<string, ValueTask<DiceValue>> lookup, Func<int, int> roll, DiceRollMode mode = DiceRollMode.Random)
	{
		var known = new Dictionary<string, DiceValue>(StringComparer.Ordinal);
		while (true)
		{
			switch (DiceParser.Parse(formula, name => known.TryGetValue(name, out var value) ? value : new Unfetched()))
			{
				case DiceNode tree:
					return new DiceEvaluator(roll, mode).Evaluate(formula, tree);
				case Error<string> error:
					return error;
				case DiceNeeds needs:
					if (known.Count + needs.Names.Length > MaxNames) return new Error<string>(ErrorMessages.Returns.RollTooManyNames);
					foreach (var name in needs.Names) known[name] = await lookup(name);
					break;
			}
		}
	}

	/// <summary>
	/// A dotted path into a JSON object, each step matched exactly first and then ignoring case.
	/// A number is a number, a string a formula, true 1 and false 0.
	/// </summary>
	public static DiceValue FromJson(JsonElement root, string path)
	{
		var current = root;
		foreach (var segment in path.Split('.'))
		{
			if (current.ValueKind != JsonValueKind.Object) return new NotFound();
			if (current.TryGetProperty(segment, out var exact))
			{
				current = exact;
				continue;
			}

			var folded = current.EnumerateObject()
				.FirstOrDefault(property => string.Equals(property.Name, segment, StringComparison.OrdinalIgnoreCase));
			if (folded.Value.ValueKind == JsonValueKind.Undefined) return new NotFound();
			current = folded.Value;
		}

		return current.ValueKind switch
		{
			JsonValueKind.Number when current.TryGetDecimal(out var number) => number,
			JsonValueKind.True => 1m,
			JsonValueKind.False => 0m,
			JsonValueKind.String => new DiceFormula(current.GetString() ?? string.Empty),
			_ => new Error<string>(string.Format(ErrorMessages.Returns.RollVariableNotANumberFormat, path.ToUpperInvariant()))
		};
	}
}

/// <summary>
/// A finished roll: the total, a one-line account of it (<c>2d6kh3[5, 3] + 2 = 10</c>) and every
/// term with its results.
/// </summary>
public sealed record DiceRollResult(string Formula, decimal Total, string Text, ImmutableArray<DiceTermResult> Terms)
{
	public string ToJson()
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
		{
			writer.WriteStartObject();
			writer.WriteString("formula", Formula);
			writer.WriteNumber("total", Total);
			writer.WriteString("text", Text);
			writer.WriteStartArray("dice");
			foreach (var term in Terms) term.Write(writer);
			writer.WriteEndArray();
			writer.WriteEndObject();
		}
		return System.Text.Encoding.UTF8.GetString(stream.ToArray());
	}
}

/// <summary>
/// One dice term or pool of a roll. <paramref name="Faces"/> is the number of sides, <c>F</c> for
/// fate dice, <c>C</c> for coins, or <c>pool</c>.
/// </summary>
public sealed record DiceTermResult(string Formula, string Faces, string? Flavor, decimal Total, ImmutableArray<DieResult> Results)
{
	internal void Write(Utf8JsonWriter writer)
	{
		writer.WriteStartObject();
		writer.WriteString("formula", Formula);
		if (int.TryParse(Faces, NumberStyles.None, CultureInfo.InvariantCulture, out var sides)) writer.WriteNumber("faces", sides);
		else writer.WriteString("faces", Faces);
		if (Flavor is { Length: > 0 }) writer.WriteString("flavor", Flavor);
		writer.WriteNumber("total", Total);
		writer.WriteStartArray("results");
		foreach (var result in Results) result.Write(writer);
		writer.WriteEndArray();
		writer.WriteEndObject();
	}
}

/// <summary>
/// One die, or one total of a pool. A result that is not <paramref name="Active"/> was dropped by a
/// keep or drop modifier or replaced by a reroll, and does not count.
/// </summary>
public sealed record DieResult(decimal Value, bool Active, bool Dropped, bool Rerolled, bool Exploded, bool Success, bool Failure)
{
	/// <summary><c>6</c>, <c>6!</c> exploded, <c>5*</c> success, <c>1_</c> failure, <c>(2)</c> not counted.</summary>
	public string Text
	{
		get
		{
			var marks = (Exploded ? "!" : "") + (Success ? "*" : "") + (Failure ? "_" : "");
			var value = MushNumber.Unparse(Value);
			return Active ? $"{value}{marks}" : $"({value}{marks})";
		}
	}

	internal void Write(Utf8JsonWriter writer)
	{
		writer.WriteStartObject();
		writer.WriteNumber("result", Value);
		writer.WriteBoolean("active", Active);
		if (Dropped) writer.WriteBoolean("dropped", true);
		if (Rerolled) writer.WriteBoolean("rerolled", true);
		if (Exploded) writer.WriteBoolean("exploded", true);
		if (Success) writer.WriteBoolean("success", true);
		if (Failure) writer.WriteBoolean("failure", true);
		writer.WriteEndObject();
	}
}
