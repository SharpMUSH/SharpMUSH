using Antlr4.Runtime.Misc;
using Serilog;

namespace AntlrCSharp.Implementation.Visitors
{
	public class PennMUSHParserVisitor(Parser parser) : PennMUSHParserBaseVisitor<IEnumerable<string>>
	{
		public override IEnumerable<string> VisitFunction([NotNull] PennMUSHParser.FunctionContext context)
		{
			var woof = context.GetText();
			var functionName = context.funName().GetText();
			var arguments = context.funArguments()?.children?.Select(x => x.GetText()).Where((_,i) => i % 2 == 0) ?? Enumerable.Empty<string>();
			var result = Functions.Functions.add(parser, arguments!.ToArray());
			Log.Logger.Information("VisitFunction: {Text} -- {Name}@{Depth}", woof, functionName, context.Depth());
			Log.Logger.Information("VisitFunction2: {@Test}", arguments);
			Log.Logger.Information("VisitFunction3: {@Result}", result);
			// A function like add() should Evaluate its contents using the function Parser, keeping the current context!
			// This means we need a way to get a hold of the Parser here.
			return [result];
		}

		public override IEnumerable<string> VisitEvaluationString([NotNull] PennMUSHParser.EvaluationStringContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitEvaluationString: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? [woof]; 
		}

		public override IEnumerable<string> VisitExplicitEvaluationString([NotNull] PennMUSHParser.ExplicitEvaluationStringContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitExplicitEvaluationString: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? [woof];
		}

		public override IEnumerable<string> VisitGenericText([NotNull] PennMUSHParser.GenericTextContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitGenericText: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? [woof];
		}

		public override IEnumerable<string> VisitValidSubstitution([NotNull] PennMUSHParser.ValidSubstitutionContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitValidSubstitution: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? [woof];
		}

		public override IEnumerable<string> VisitCommand([NotNull] PennMUSHParser.CommandContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitCommand: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? [woof];
		}

		public override IEnumerable<string> VisitCommandString([NotNull] PennMUSHParser.CommandStringContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitCommandString: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? [woof];
		}
		public override IEnumerable<string> VisitCommandList([NotNull] PennMUSHParser.CommandListContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitCommandList: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? [woof];
		}
		public override IEnumerable<string> VisitSingleCommandString([NotNull] PennMUSHParser.SingleCommandStringContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitSingleCommandString: {Text}", woof);
			var children = base.VisitChildren(context);
			return children ?? [woof];
		}

	}
}