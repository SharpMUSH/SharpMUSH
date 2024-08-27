using Antlr4.Runtime.Misc;
using Serilog;
using SharpMUSH.Library.ParserInterfaces;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Visitors
{
	/// <summary>
	/// This class implements the SharpMUSHParserBaseVisitor from the Generated code.
	/// If additional pieces of the parse-tree are added, the Generated project must be re-generated 
	/// and new Visitors may need to be added.
	/// </summary>
	/// <param name="parser">The Parser, so that inner functions can force a parser-call.</param>
	public class SharpMUSHParserVisitor(IMUSHCodeParser parser, MString source) : SharpMUSHParserBaseVisitor<CallState?>
	{
		protected override CallState? AggregateResult(CallState? aggregate, CallState? nextResult)
		{
			if (aggregate?.Arguments != null || nextResult?.Arguments != null)
			{
				return (aggregate ?? nextResult!) with
				{
					Arguments = [
					.. aggregate?.Arguments ?? Enumerable.Empty<MString>(),
					.. nextResult?.Arguments ?? Enumerable.Empty<MString>()
					]
				};
			}
			if (aggregate?.Message != null && nextResult?.Message != null)
			{
				return aggregate with { Message = MModule.concat(aggregate.Message, nextResult.Message) };
			}
			return aggregate ?? nextResult ?? null;
		}

		public override CallState? VisitFunction([NotNull] SharpMUSHParser.FunctionContext context)
		{
			var functionName = context.funName().GetText().TrimEnd()[..^1];
			var arguments = context.funArguments()?.evaluationString()?
				.Select(x => new CallState(MModule.substring(x.Start.StartIndex, context.Stop?.StopIndex == null ? 0 : (x.Stop.StopIndex - x.Start.StartIndex + 1), source), context.Depth()))
				?? [new(MModule.empty(), context.Depth())];

			Log.Logger.Information("VisitFunction: Fun: {Text}, Args: {Args}", functionName, arguments.Select( x => x.Message!.ToString()));
			var result = Functions.Functions.CallFunction(functionName.ToLower(), source, parser, context, arguments.ToList());
			return result;
		}

		public override CallState? VisitEvaluationString([NotNull] SharpMUSHParser.EvaluationStringContext context)
			=> base.VisitChildren(context)
					?? new(MModule.substring(context.Start.StartIndex, context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source), context.Depth());

		public override CallState? VisitExplicitEvaluationString([NotNull] SharpMUSHParser.ExplicitEvaluationStringContext context)
			=> base.VisitChildren(context)
					?? new(MModule.substring(context.Start.StartIndex, context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source), context.Depth());

		public override CallState? VisitGenericText([NotNull] SharpMUSHParser.GenericTextContext context)
			=> base.VisitChildren(context)
					?? new(MModule.substring(context.Start.StartIndex, context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source), context.Depth());

		public override CallState? VisitStartGenericText([NotNull] SharpMUSHParser.StartGenericTextContext context)
			=> base.VisitChildren(context)
					?? new(MModule.substring(context.Start.StartIndex, context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source), context.Depth());

		public override CallState? VisitValidSubstitution([NotNull] SharpMUSHParser.ValidSubstitutionContext context)
		{
			var textContents = MModule.single(context.GetText());
			var complexSubstitutionSymbol = context.complexSubstitutionSymbol();
			var simpleSubstitutionSymbol = context.substitutionSymbol();

			if (complexSubstitutionSymbol != null)
			{
				var state = VisitChildren(context);
				var result = Substitutions.Substitutions.ParseComplexSubstitution(state, parser, complexSubstitutionSymbol);
				return result;
			}
			else if (simpleSubstitutionSymbol != null)
			{
				var result = Substitutions.Substitutions.ParseSimpleSubstitution(simpleSubstitutionSymbol.GetText(), parser, simpleSubstitutionSymbol);
				return result;
			}
			else
			{
				var children = base.VisitChildren(context);
				return children ?? new(textContents, context.Depth());
			}
		}

		public override CallState? VisitCommand([NotNull] SharpMUSHParser.CommandContext context)
		{
			var text = MModule.substring(context.Start.StartIndex, context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source);
			Log.Logger.Information("VisitCommand: {Text}", text);

			return Commands.Commands.EvaluateCommands(parser, source, context, base.VisitChildren).Match(
				x => (CallState?)null,
				x => x.Value);
		}

		public override CallState? VisitStartCommandString([NotNull] SharpMUSHParser.StartCommandStringContext context)
		{
			var text = MModule.substring(context.Start.StartIndex, context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source);
			Log.Logger.Information("VisitCommandString: {Text}", text);
			var children = base.VisitChildren(context);
			return children ?? new CallState(text, context.Depth());
		}

		public override CallState? VisitCommandList([NotNull] SharpMUSHParser.CommandListContext context)
		{
			var text = MModule.substring(context.Start.StartIndex, context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source);
			Log.Logger.Information("VisitCommandList: {Text}", text);
			var children = base.VisitChildren(context);
			return children ?? new CallState(text, context.Depth());
		}

		public override CallState? VisitStartSingleCommandString([NotNull] SharpMUSHParser.StartSingleCommandStringContext context)
		{
			var text = MModule.substring(context.Start.StartIndex, context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source);
			Log.Logger.Information("VisitSingleCommandString: {Text}", text);
			var children = base.VisitChildren(context);
			return children ?? new CallState(text, context.Depth());
		}

		public override CallState? VisitEscapedText([NotNull] SharpMUSHParser.EscapedTextContext context)
			=> base.VisitChildren(context)
				?? new(MModule.substring(context.Start.StartIndex + 1, context.Stop.StopIndex - context.Start.StartIndex + 1 - 1, source), context.Depth());

		/// <summary>
		/// Visit a parse tree produced by <see cref="SharpMUSHParser.singleCommandArg"/>.
		/// <para>
		/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
		/// on <paramref name="context"/>.
		/// </para>
		/// </summary>
		/// <param name="context">The parse tree.</param>
		/// <return>The visitor result.</return>
		public override CallState? VisitSingleCommandArg([NotNull] SharpMUSHParser.SingleCommandArgContext context)
			=> new(null, context.Depth(), [MModule.substring(context.Start.StartIndex, context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source)]);

		/// <summary>
		/// Visit a parse tree produced by <see cref="SharpMUSHParser.eqsplitCommandArgs"/>.
		/// <para>
		/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
		/// on <paramref name="context"/>.
		/// </para>
		/// </summary>
		/// <param name="context">The parse tree.</param>
		/// <return>The visitor result.</return>
		public override CallState? VisitStartEqSplitCommandArgs([NotNull] SharpMUSHParser.StartEqSplitCommandArgsContext context)
		{
			var baseArg = base.VisitChildren(context.singleCommandArg());
			var commaArgs = base.VisitChildren(context.commaCommandArgs());
			Log.Logger.Information("VisitEqsplitCommandArgs: C1: {Text} - C2: {Text2}", baseArg?.ToString(), commaArgs?.ToString());
			return new(null, context.Depth(), [baseArg!.Message!, .. commaArgs?.Arguments ?? []]);
		}

		/// <summary>
		/// Visit a parse tree produced by <see cref="SharpMUSHParser.eqsplitCommand"/>.
		/// <para>
		/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
		/// on <paramref name="context"/>.
		/// </para>
		/// </summary>
		/// <param name="context">The parse tree.</param>
		/// <return>The visitor result.</return>
		public override CallState? VisitStartEqSplitCommand([NotNull] SharpMUSHParser.StartEqSplitCommandContext context)
		{
			var baseArg = base.VisitChildren(context.singleCommandArg()[0]);
			var rsArg = base.VisitChildren(context.singleCommandArg()[1]);
			Log.Logger.Information("VisitEqsplitCommand: C1: {Text} - C2: {Text2}", baseArg?.ToString(), rsArg?.ToString());
			return new(null, context.Depth(), [baseArg!.Message!, rsArg!.Message!]);
		}

		/// <summary>
		/// Visit a parse tree produced by <see cref="SharpMUSHParser.commaCommandArgs"/>.
		/// <para>
		/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
		/// on <paramref name="context"/>.
		/// </para>
		/// </summary>
		/// <param name="context">The parse tree.</param>
		/// <return>The visitor result.</return>
		public override CallState? VisitCommaCommandArgs([NotNull] SharpMUSHParser.CommaCommandArgsContext context)
		{
			var children = base.VisitChildren(context);
			return new(null, context.Depth(), children!.Arguments);
		}

		public override CallState? VisitComplexSubstitutionSymbol([NotNull] ComplexSubstitutionSymbolContext context)
		{
			if (context.ChildCount > 1)
				return base.VisitChildren(context);
			else if (context.REG_NUM() is not null)
				return new(MModule.substring(context.Start.StartIndex + 1, context.Stop.StopIndex - context.Start.StartIndex + 1 - 1, source), context.Depth());
			else if (context.ITEXT_NUM() is not null || context.STEXT_NUM() is not null)
				return new(MModule.substring(context.Start.StartIndex + 1, context.Stop.StopIndex - context.Start.StartIndex + 1 - 1, source), context.Depth());
			else
				return new(MModule.substring(context.Start.StartIndex, context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source), context.Depth());
		}
	}
}