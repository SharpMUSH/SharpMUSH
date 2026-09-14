using Antlr4.Runtime;
using Antlr4.Runtime.Misc;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>Reads lock operands and values without losing whitespace between punctuation tokens.</summary>
internal static class LockLiteralText
{
	public static string ReadOperand(SharpMUSHBoolExpParser.ObjectOperandContext context)
		=> context.Start.InputStream.GetText(Interval.Of(context.Start.StartIndex, context.Stop.StopIndex));

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
