using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Definitions;

/// <summary>
/// The names an event handler's attribute reads its arguments by, as %&lt;name&gt;, beside %0-%9, from
/// <see cref="EventCatalogue"/>. Each event's names are in the order of its arguments, as <c>help event list</c>
/// gives them; PennMUSH's events keep their positions, so code written for PennMUSH still reads %0-%9.
/// </summary>
public static class EventArguments
{
	/// <summary>The names <paramref name="eventName"/>'s arguments are passed under, in order; none for an event not listed.</summary>
	public static IReadOnlyList<string> For(string eventName)
		=> EventCatalogue.Find(eventName)?.ArgumentNames ?? [];

	/// <summary>
	/// <paramref name="arguments"/> as an event handler receives them: by position as %0-%9, and by the
	/// event's names.
	/// </summary>
	public static Dictionary<string, CallState> Of(string eventName, IReadOnlyList<string> arguments)
		=> Of(eventName, arguments.Select(MarkupText.Plain).ToArray());

	/// <summary>
	/// <paramref name="arguments"/> as an event handler receives them, keeping their markup: by position as
	/// %0-%9, and by the event's names. The parts of a value joined by <c>/</c> are plain numbers.
	/// </summary>
	public static Dictionary<string, CallState> Of(string eventName, IReadOnlyList<MString> arguments)
	{
		var named = arguments.Index().ToDictionary(x => x.Index.ToString(), x => new CallState(x.Item), StringComparer.OrdinalIgnoreCase);
		foreach (var (name, value) in For(eventName).Zip(arguments))
		{
			if (!name.Contains('/'))
			{
				named[name] = new CallState(value);
				continue;
			}

			foreach (var (part, partValue) in Parts(name, value.ToPlainText()))
			{
				named[part] = new CallState(partValue);
			}
		}

		return named;
	}

	private static IEnumerable<(string Name, string Value)> Parts(string name, string value)
	{
		var values = value.Split('/');
		return name.Split('/').Select((part, index) => (part, index < values.Length ? values[index] : string.Empty));
	}
}
