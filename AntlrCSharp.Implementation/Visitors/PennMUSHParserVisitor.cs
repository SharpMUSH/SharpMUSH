using Antlr4.Runtime.Misc;
using Serilog;

namespace AntlrCSharp.Implementation.Visitors
{
	/// <summary>
	/// This class implements the PennMUSHParserBaseVisitor from the Generated code.
	/// If additional pieces of the parse-tree are added, the Generated project must be re-generated 
	/// and new Visitors may need to be added.
	/// </summary>
	/// <param name="parser">The Parser, so that inner functions can force a parser-call.</param>
	public class PennMUSHParserVisitor(Parser parser) : PennMUSHParserBaseVisitor<CallState?>
	{
		protected override CallState? AggregateResult(CallState? aggregate, CallState? nextResult)
		{
			if (aggregate?.Message != null && nextResult?.Message != null)
			{
				return aggregate with { Message = MModule.concat(aggregate.Message, nextResult.Message) };
			}
			return aggregate ?? nextResult ?? null;
		}

		public override CallState? VisitFunction([NotNull] PennMUSHParser.FunctionContext context)
		{
			// TODO: There needs to be a standard behavior for functions, rather than making /each/ function call their contents.
			//       Instead, a function like if() should be explicitly stating if a function call needs to be evaluated or not.
			var textContents = context.GetText();
			var functionName = context.funName().GetText();
			var arguments = context.funArguments()?.children?
				.Where((_, i) => i % 2 == 0)
				.Select(x => new CallState(x.GetText(), context.Depth()))
				?? [new CallState("", context.Depth())];

			// TODO: There seems to be a standard in PennMUSH that if there is no argument, it passes in an Empty String
			// as the one and only argument.

			var result = Functions.Functions.CallFunction(functionName.ToLower(), parser, context, arguments.ToArray());
			Log.Logger.Information("VisitFunction: {@Text} -- {Name}@{Depth}", textContents, functionName, context.Depth());
			Log.Logger.Information("VisitFunction2: {@Test}", arguments);
			Log.Logger.Information("VisitFunction3: {@Result}", result);
			return result;
		}

		public override CallState? VisitEvaluationString([NotNull] PennMUSHParser.EvaluationStringContext context)
		{
			var children = base.VisitChildren(context);

			if (children is not null)
			{
				return children;
			}
			else
			{
				return new CallState(context.GetText(), context.Depth());
			}
		}

		public override CallState? VisitExplicitEvaluationString([NotNull] PennMUSHParser.ExplicitEvaluationStringContext context)
		{
			var children = base.VisitChildren(context);

			if (children is not null)
			{
				return children;
			}
			else
			{
				return new CallState(context.GetText(), context.Depth());
			}
		}

		public override CallState? VisitGenericText([NotNull] PennMUSHParser.GenericTextContext context)
		{
			var children = base.VisitChildren(context);
			return children ?? new CallState(context.GetText(), context.Depth());
		}

		public override CallState? VisitValidSubstitution([NotNull] PennMUSHParser.ValidSubstitutionContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitValidSubstitution: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? new CallState(woof, context.Depth());
		}

		public override CallState? VisitCommand([NotNull] PennMUSHParser.CommandContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitCommand: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? new CallState(woof, context.Depth());
		}

		public override CallState? VisitCommandString([NotNull] PennMUSHParser.CommandStringContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitCommandString: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? new CallState(woof, context.Depth());
		}

		public override CallState? VisitCommandList([NotNull] PennMUSHParser.CommandListContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitCommandList: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? new CallState(woof, context.Depth());
		}

		public override CallState? VisitSingleCommandString([NotNull] PennMUSHParser.SingleCommandStringContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitSingleCommandString: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? new CallState(woof, context.Depth());
		}

		public override CallState? VisitEscapedText([NotNull] PennMUSHParser.EscapedTextContext context)
		{
			var children = base.VisitChildren(context);
			return children ?? new CallState(context.GetText().Remove(0, 1), context.Depth());
		}
	}
}