using Antlr4.Runtime.Misc;
using Serilog;

namespace AntlrCSharp.Implementation.Visitors
{
	public class PennMUSHParserVisitor(Parser parser) : PennMUSHParserBaseVisitor<CallState?>
	{
		protected override CallState? AggregateResult(CallState? aggregate, CallState? nextResult)
		{
			if (aggregate?.Message != null && nextResult?.Message != null)
			{
				return aggregate with { Message = string.Concat(aggregate?.Message, nextResult?.Message) };
			}
			return aggregate ?? nextResult ?? null;
		}

		public override CallState? VisitFunction([NotNull] PennMUSHParser.FunctionContext context)
		{
			var woof = context.GetText();
			var functionName = context.funName().GetText();
			var arguments = context.funArguments()?.children?
				.Where((_, i) => i % 2 == 0)
				.Select(x => new CallState(x.GetText()))
				?? Enumerable.Empty<CallState>();
			var result = Functions.Functions.add(parser, arguments!.ToArray());
			Log.Logger.Information("VisitFunction: {Text} -- {Name}@{Depth}", woof, functionName, context.Depth());
			Log.Logger.Information("VisitFunction2: {@Test}", arguments);
			Log.Logger.Information("VisitFunction3: {@Result}", result);
			return result;
		}

		public override CallState? VisitEvaluationString([NotNull] PennMUSHParser.EvaluationStringContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitEvaluationString: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? new CallState(woof);
		}

		public override CallState? VisitExplicitEvaluationString([NotNull] PennMUSHParser.ExplicitEvaluationStringContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitExplicitEvaluationString: {Text}", woof);
			var children = base.VisitChildren(context); // This is returning Null
			return children ?? new CallState(woof);
		}

		public override CallState? VisitGenericText([NotNull] PennMUSHParser.GenericTextContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitGenericText: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? new CallState(woof);
		}

		public override CallState? VisitValidSubstitution([NotNull] PennMUSHParser.ValidSubstitutionContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitValidSubstitution: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? new CallState(woof);
		}

		public override CallState? VisitCommand([NotNull] PennMUSHParser.CommandContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitCommand: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? new CallState(woof);
		}

		public override CallState? VisitCommandString([NotNull] PennMUSHParser.CommandStringContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitCommandString: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? new CallState(woof);
		}
		public override CallState? VisitCommandList([NotNull] PennMUSHParser.CommandListContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitCommandList: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? new CallState(woof);
		}
		public override CallState? VisitSingleCommandString([NotNull] PennMUSHParser.SingleCommandStringContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitSingleCommandString: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? new CallState(woof);
		}

	}
}