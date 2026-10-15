using System.Text.Json;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Implementation.Dice;

public enum RollOutput
{
	Total,
	Text,
	Json
}

/// <summary>
/// roll()'s third argument: a word (<c>total</c>, <c>text</c>, <c>json</c>, <c>min</c>, <c>max</c>)
/// or a JSON object, <c>{"output":"json","mode":"max","record":true}</c>, as the layout functions take
/// options. A recorded roll answers in JSON, which carries its id.
/// </summary>
public sealed record RollOptions(RollOutput Output, DiceRollMode Mode, bool Record = false)
{
	public static Result<RollOptions> Read(string options)
	{
		if (!options.StartsWith('{')) return FromWords(options, string.Empty);

		try
		{
			using var document = JsonDocument.Parse(options);
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) return new Error<string>(ErrorMessages.Returns.RollOutputMode);
			var output = Member(root, "output");
			var read = FromWords(output, Member(root, "mode"));
			if (!root.TryGetProperty("record", out var record) || record.ValueKind != JsonValueKind.True) return read;

			return read switch
			{
				RollOptions { Mode: not DiceRollMode.Random } => new Error<string>(ErrorMessages.Returns.RollRecordOnlyRolls),
				RollOptions { Output: not RollOutput.Json } when output.Length > 0
					=> new Error<string>(ErrorMessages.Returns.RollRecordAnswersJson),
				RollOptions rolled => rolled with { Output = RollOutput.Json, Record = true },
				Error<string> error => error
			};
		}
		catch (JsonException)
		{
			return new Error<string>(ErrorMessages.Returns.RollInvalidJson);
		}
	}

	private static string Member(JsonElement root, string name)
		=> root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString() ?? string.Empty
			: string.Empty;

	private static Result<RollOptions> FromWords(string output, string mode)
	{
		var range = mode.Trim().ToLowerInvariant() switch
		{
			"" or "roll" => DiceRollMode.Random,
			"min" => DiceRollMode.Minimize,
			"max" => DiceRollMode.Maximize,
			_ => (DiceRollMode?)null
		};

		return (output.Trim().ToLowerInvariant(), range) switch
		{
			(_, null) => new Error<string>(ErrorMessages.Returns.RollOutputMode),
			("" or "total", DiceRollMode given) => new RollOptions(RollOutput.Total, given),
			("text", DiceRollMode given) => new RollOptions(RollOutput.Text, given),
			("json", DiceRollMode given) => new RollOptions(RollOutput.Json, given),
			("min", DiceRollMode.Random) => new RollOptions(RollOutput.Total, DiceRollMode.Minimize),
			("max", DiceRollMode.Random) => new RollOptions(RollOutput.Total, DiceRollMode.Maximize),
			_ => new Error<string>(ErrorMessages.Returns.RollOutputMode)
		};
	}
}
