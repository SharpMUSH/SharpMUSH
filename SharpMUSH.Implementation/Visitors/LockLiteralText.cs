using Antlr4.Runtime;
using Antlr4.Runtime.Misc;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>Reads lock operands and values without losing whitespace between punctuation tokens.</summary>
internal static class LockLiteralText
{
	public static string ReadOperand(SharpMUSHBoolExpParser.ObjectOperandContext context)
		=> context.Start.InputStream.GetText(Interval.Of(context.Start.StartIndex, context.Stop.StopIndex));

	/// <summary>
	/// An attribute name in an attribute or evaluation key, with each backslash escape replaced by
	/// the character it escapes, as test_atr (src/boolexp.c) reads it: <c>\+FOO:BAR</c> names <c>+FOO</c>.
	/// </summary>
	public static string ReadAttributeName(SharpMUSHBoolExpParser.StringContext context)
	{
		var text = context.GetText();
		if (!text.Contains('\\')) return text;

		var name = new System.Text.StringBuilder(text.Length);
		for (var index = 0; index < text.Length; index++)
		{
			if (text[index] == '\\' && index + 1 < text.Length) index++;
			name.Append(text[index]);
		}

		return name.ToString();
	}

	public static string Read(SharpMUSHBoolExpParser.LiteralContext context)
	{
		var delimiter = context.Parent switch
		{
			SharpMUSHBoolExpParser.AttributeExprContext attribute => attribute.ATTRIBUTE_COLON().Symbol,
			SharpMUSHBoolExpParser.EvaluationExprContext evaluation => evaluation.EVALUATION().Symbol,
			_ => ((ParserRuleContext)context.Parent).Start
		};
		return context.Start.InputStream.GetText(Interval.Of(delimiter.StopIndex + 1, context.Stop.StopIndex)).TrimEnd();
	}
}
