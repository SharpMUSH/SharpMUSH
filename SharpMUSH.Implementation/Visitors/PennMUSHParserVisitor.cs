using Antlr4.Runtime.Misc;
using Serilog;

namespace SharpMUSH.Implementation.Visitors
{
	/// <summary>
	/// This class implements the SharpMUSHParserBaseVisitor from the Generated code.
	/// If additional pieces of the parse-tree are added, the Generated project must be re-generated 
	/// and new Visitors may need to be added.
	/// </summary>
	/// <param name="parser">The Parser, so that inner functions can force a parser-call.</param>
	public class SharpMUSHParserVisitor(Parser parser) : SharpMUSHParserBaseVisitor<CallState?>
	{
		protected override CallState? AggregateResult(CallState? aggregate, CallState? nextResult)
		{
			if (aggregate?.Message != null && nextResult?.Message != null)
			{
				return aggregate with { Message = MModule.concat(aggregate.Message, nextResult.Message) };
			}
			return aggregate ?? nextResult ?? null;
		}

		public override CallState? VisitFunction([NotNull] SharpMUSHParser.FunctionContext context)
		{
			var textContents = context.GetText();
			var functionName = context.funName().GetText();
			var arguments = context.funArguments()?.children?
				.Where((_, i) => i % 2 == 0)
				.Select(x => new CallState(x.GetText(), context.Depth()))
				?? [new CallState("", context.Depth())];

			var result = Functions.Functions.CallFunction(functionName.ToLower(), parser, context, arguments.ToArray());
			return result;
		}

		public override CallState? VisitEvaluationString([NotNull] SharpMUSHParser.EvaluationStringContext context) 
			=> base.VisitChildren(context) ?? new CallState(context.GetText(), context.Depth());

		public override CallState? VisitExplicitEvaluationString([NotNull] SharpMUSHParser.ExplicitEvaluationStringContext context)
			=> base.VisitChildren(context) ?? new CallState(context.GetText(), context.Depth());

		public override CallState? VisitGenericText([NotNull] SharpMUSHParser.GenericTextContext context)
			=> base.VisitChildren(context) ?? new CallState(context.GetText(), context.Depth());

		public override CallState? VisitValidSubstitution([NotNull] SharpMUSHParser.ValidSubstitutionContext context)
		{
			var textContents = context.GetText();
			var complexSubstitutionSymbol = context.complexSubstitutionSymbol();
			var simpleSubstitutionSymbol = context.substitutionSymbol();

			if (complexSubstitutionSymbol  != null )
			{
				var result = Substitutions.Substitutions.ParseComplexSubstitution(complexSubstitutionSymbol.GetText(), parser, complexSubstitutionSymbol);
				return result;
			}
			else if(simpleSubstitutionSymbol != null)
			{
				var result = Substitutions.Substitutions.ParseSimpleSubstitution(simpleSubstitutionSymbol.GetText(), parser, simpleSubstitutionSymbol);
				return result;
			}
			else
			{
				var children = base.VisitChildren(context);
				return children ?? new CallState(textContents, context.Depth());
			}
		}

		public override CallState? VisitCommand([NotNull] SharpMUSHParser.CommandContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitCommand: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? new CallState(woof, context.Depth());
		}

		public override CallState? VisitCommandString([NotNull] SharpMUSHParser.CommandStringContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitCommandString: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? new CallState(woof, context.Depth());
		}

		public override CallState? VisitCommandList([NotNull] SharpMUSHParser.CommandListContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitCommandList: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? new CallState(woof, context.Depth());
		}

		public override CallState? VisitSingleCommandString([NotNull] SharpMUSHParser.SingleCommandStringContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitSingleCommandString: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? new CallState(woof, context.Depth());
		}

		public override CallState? VisitEscapedText([NotNull] SharpMUSHParser.EscapedTextContext context) 
			=> base.VisitChildren(context) ?? new CallState(context.GetText().Remove(0, 1), context.Depth());
	}
}