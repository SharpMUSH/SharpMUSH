using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Definitions;

/// <summary>What a bang tests its value for.</summary>
public enum BangTest
{
	/// <summary><c>!</c>, <c>!!</c>: the value's leading whole number is not zero.</summary>
	Number,

	/// <summary><c>!$</c>, <c>!!$</c>: the value is not empty.</summary>
	Empty,

	/// <summary><c>!^</c>, <c>!!^</c>: the value is true, as <c>t()</c> reads it.</summary>
	Truth
}

/// <summary>
/// Bang notation, an idea from RhostMUSH: a function call written <c>!name(...)</c> answers 1 or 0 about its
/// result instead of the result itself. One <c>!</c> negates the test and two keep it; a <c>$</c> or <c>^</c>
/// after them picks the test (<see cref="BangTest"/>). The same prefix works inside <c>%q&lt;...&gt;</c>,
/// <c>%i</c> and <c>%d</c>.
/// </summary>
public readonly record struct Bang(bool Negate, BangTest Test)
{
	/// <summary>
	/// Reads a bang prefix from the start of <paramref name="text"/>: how many characters it takes, 0 when
	/// there is none.
	/// </summary>
	public static int Read(ReadOnlySpan<char> text, out Bang bang)
	{
		bang = default;
		var keep = text.StartsWith("!!");
		var length = keep ? 2 : text.StartsWith("!") ? 1 : 0;
		if (length == 0) return 0;

		var test = BangTest.Number;
		if (text.Length > length && text[length] is '$' or '^')
		{
			test = text[length] == '$' ? BangTest.Empty : BangTest.Truth;
			length++;
		}

		bang = new Bang(Negate: !keep, test);
		return length;
	}

	/// <summary>The function a call names once its bang prefix is set aside.</summary>
	public static string FunctionName(string name) => name[Read(name, out _)..];

	/// <summary>1 or 0 for <paramref name="value"/>.</summary>
	public MString Apply(MString value, IMUSHCodeParser parser)
	{
		var holds = Test switch
		{
			BangTest.Number => Predicates.Truthy(value, tinyBooleans: true),
			BangTest.Empty => value.Length > 0,
			_ => value.Truthy(parser)
		};
		return MarkupText.Plain(holds != Negate ? "1" : "0");
	}
}
