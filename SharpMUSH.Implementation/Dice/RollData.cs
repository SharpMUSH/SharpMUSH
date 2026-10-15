using System.Text.Json;
using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Dice;

/// <summary>
/// What roll() and <c>@roll</c> share: reading a formula's data (nothing, a JSON object, or an object's
/// attributes) and keeping a roll.
/// </summary>
public static class RollData
{
	/// <summary>
	/// Rolls <paramref name="formula"/> with <paramref name="data"/>: empty, a JSON object, or an object
	/// <paramref name="executor"/> can find, whose attributes it reads as get() does.
	/// </summary>
	public static async ValueTask<Result<DiceRollResult>> RollAsync(IMUSHCodeParser parser, AnySharpObject executor,
		ILocateService locate, IAttributeService attributes, string formula, string data, DiceRollMode mode)
	{
		if (data.Length == 0) return await DiceRoll.RollAsync(formula, NoData, RollDie, mode);
		if (data[0] is '{' or '[') return await RollWithJsonAsync(formula, data, mode);

		return await locate.LocateAndNotifyIfInvalid(parser, executor, executor, data, LocateFlags.All) switch
		{
			AnySharpObject holder => await DiceRoll.RollAsync(formula,
				name => FromAttributeAsync(attributes, executor, holder, name), RollDie, mode),
			Error<string> error => error,
			_ => new Error<string>(ErrorMessages.Returns.NoMatch)
		};
	}

	/// <summary>
	/// Keeps <paramref name="result"/> as rolled for <paramref name="enactor"/> by
	/// <paramref name="executor"/>'s code, at the time the store's handler gives it.
	/// </summary>
	public static async ValueTask<RecordedRoll> RecordAsync(IMediator mediator, AnySharpObject enactor,
		AnySharpObject executor, DiceRollResult result)
	{
		var roller = enactor.Object();
		var by = executor.Object().DBRef;
		var draft = new RollDraft(default, roller.DBRef.ToString(), roller.Name,
			by.Equals(roller.DBRef) ? null : by.ToString(), result.Formula, result.Total, result.Text, result.ToJson());
		return await mediator.Send(new RecordRollCommand(draft));
	}

	/// <summary>
	/// A kept roll as rollinfo() and roll()'s <c>record</c> option answer it: roll()'s JSON with the
	/// id, when, and who first.
	/// </summary>
	public static string ToJson(RecordedRoll recorded)
	{
		var roll = recorded.Roll;
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream,
			new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
		{
			writer.WriteStartObject();
			writer.WriteNumber("id", recorded.Id);
			writer.WriteString("at", roll.At.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));
			writer.WriteString("roller", roll.RollerObjid);
			writer.WriteString("name", roll.RollerName);
			if (roll.ObjectObjid is { } by) writer.WriteString("by", by);

			using var result = JsonDocument.Parse(roll.Json);
			foreach (var member in result.RootElement.EnumerateObject()) member.WriteTo(writer);
			writer.WriteEndObject();
		}

		return System.Text.Encoding.UTF8.GetString(stream.ToArray());
	}

	private static async ValueTask<Result<DiceRollResult>> RollWithJsonAsync(string formula, string data, DiceRollMode mode)
	{
		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(data);
		}
		catch (JsonException)
		{
			return new Error<string>(ErrorMessages.Returns.RollInvalidJson);
		}

		using (document)
		{
			var root = document.RootElement;
			return root.ValueKind == JsonValueKind.Object
				? await DiceRoll.RollAsync(formula, name => ValueTask.FromResult(DiceRoll.FromJson(root, name)), RollDie, mode)
				: new Error<string>(ErrorMessages.Returns.RollDataMustBeObject);
		}
	}

	/// <summary>
	/// <c>stat.dex</c> is the attribute <c>STAT`DEX</c>, read with the visibility get() checks and
	/// never evaluated.
	/// </summary>
	private static async ValueTask<DiceValue> FromAttributeAsync(IAttributeService attributes, AnySharpObject executor,
		AnySharpObject holder, string name)
	{
		var attribute = name.Replace('.', '`').ToUpperInvariant();
		return await attributes.GetAttributeAsync(executor, holder, attribute,
				mode: IAttributeService.AttributeMode.Read, parent: true) switch
		{
			SharpAttribute[] chain when chain.Last().Value.ToPlainText().Trim() is { Length: > 0 } text => new DiceFormula(text),
			SharpAttribute[] or None => new NotFound(),
			Error<string> error => error
		};
	}

	private static ValueTask<DiceValue> NoData(string name) => ValueTask.FromResult<DiceValue>(new NotFound());

	private static int RollDie(int sides) => Random.Shared.Next(1, sides + 1);
}
