namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>
/// The arguments of an attribute the game evaluates as a callback (a format, a mogrifier, a filter):
/// each value by its position as %0-%9, which code written for PennMUSH reads, and by its name as
/// %&lt;name&gt;. Names are case-insensitive, as %&lt;name&gt; reads them.
/// </summary>
public static class CallbackArguments
{
	/// <summary><paramref name="values"/> in order: the first is %0 and its name, the second %1 and its name.</summary>
	public static Dictionary<string, CallState> Of(params ReadOnlySpan<(string Name, CallState Value)> values)
	{
		var arguments = new Dictionary<string, CallState>(StringComparer.OrdinalIgnoreCase);
		for (var position = 0; position < values.Length; position++)
		{
			arguments.Set(position, values[position].Name, values[position].Value);
		}

		return arguments;
	}

	/// <summary>Replaces the argument at <paramref name="position"/>, and its name, with <paramref name="value"/>.</summary>
	public static void Set(this Dictionary<string, CallState> arguments, int position, string name, CallState value)
	{
		arguments[position.ToString()] = value;
		arguments[name] = value;
	}
}
