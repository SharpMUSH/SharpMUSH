using Antlr4.Runtime.Misc;
using Serilog;
using System.Collections.Immutable;

namespace AntlrCSharp.Implementation.Visitors
{
	public class PennMUSHParserVisitor : PennMUSHParserBaseVisitor<IImmutableList<string>>
	{
		public override IImmutableList<string> VisitFunction([NotNull] PennMUSHParser.FunctionContext context)
		{
			var woof = context.GetText();
			var functionName = context.funName().GetText();
			Log.Logger.Information("VisitFunction: {Text} -- {Name}@{Depth}", woof, functionName, context.Depth());
			Log.Logger.Information("VisitFunction2: {@Test}", context.funArguments()?.children?.Select(x => x.GetText()));
			return base.VisitFunction(context) ?? ImmutableList.Create<string>().Add($"FUNC::{woof}");
		}

		public override IImmutableList<string> VisitFunArguments([NotNull] PennMUSHParser.FunArgumentsContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitFunArguments: {Text}", woof);
			return base.VisitChildren(context) ?? ImmutableList.Create<string>().Add(woof);
		}

		public override IImmutableList<string> VisitEvaluationString([NotNull] PennMUSHParser.EvaluationStringContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitEvaluationString: {Text}", woof);
			return base.VisitChildren(context) ?? ImmutableList.Create<string>().Add(woof); 
		}

		public override IImmutableList<string> VisitExplicitEvaluationString([NotNull] PennMUSHParser.ExplicitEvaluationStringContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitExplicitEvaluationString: {Text}", woof);
			return base.VisitChildren(context) ?? ImmutableList.Create<string>().Add(woof); 
		}

		public override IImmutableList<string> VisitGenericText([NotNull] PennMUSHParser.GenericTextContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitGenericText: {Text}", woof);
			return base.VisitChildren(context) ?? ImmutableList.Create<string>().Add(woof);
		}

		public override IImmutableList<string> VisitValidSubstitution([NotNull] PennMUSHParser.ValidSubstitutionContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitValidSubstitution: {Text}", woof);
			return base.VisitChildren(context) ?? ImmutableList.Create<string>().Add(woof);
		}

		public override IImmutableList<string> VisitCommand([NotNull] PennMUSHParser.CommandContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitCommand: {Text}", woof);
			return base.VisitChildren(context) ?? ImmutableList.Create<string>().Add(woof);
		}

		public override IImmutableList<string> VisitCommandString([NotNull] PennMUSHParser.CommandStringContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitCommandString: {Text}", woof);
			return base.VisitChildren(context) ?? ImmutableList.Create<string>().Add(woof);
		}
		public override IImmutableList<string> VisitCommandList([NotNull] PennMUSHParser.CommandListContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitCommandList: {Text}", woof);
			return base.VisitChildren(context) ?? ImmutableList.Create<string>().Add(woof);
		}
		public override IImmutableList<string> VisitSingleCommandString([NotNull] PennMUSHParser.SingleCommandStringContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitSingleCommandString: {Text}", woof);
			return base.VisitChildren(context) ?? ImmutableList.Create<string>().Add(woof);
		}

	}
}