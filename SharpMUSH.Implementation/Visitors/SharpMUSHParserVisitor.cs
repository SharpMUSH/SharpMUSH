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
	public class SharpMUSHParserVisitor(IMUSHCodeParser parser) : SharpMUSHParserBaseVisitor<CallState?>
	{
		protected override CallState? AggregateResult(CallState? aggregate, CallState? nextResult)
		{
			if (aggregate?.Arguments != null || nextResult?.Arguments != null)
			{
				return (aggregate ?? nextResult!) with { Arguments = [.. aggregate?.Arguments ?? Enumerable.Empty<string>(), .. nextResult?.Arguments ?? Enumerable.Empty<string>()] };
			}
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

			var result = Functions.Functions.CallFunction(functionName.ToLower(), parser, context, arguments.ToList());
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

			if (complexSubstitutionSymbol != null)
			{
				var result = Substitutions.Substitutions.ParseComplexSubstitution(complexSubstitutionSymbol.GetText(), parser, complexSubstitutionSymbol);
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
				return children ?? new CallState(textContents, context.Depth());
			}
		}

		public override CallState? VisitCommand([NotNull] SharpMUSHParser.CommandContext context)
		{
			var woof = context.GetText();
			Log.Logger.Information("VisitCommand: {Text}", woof);

			return Commands.Commands.EvaluateCommands(parser, context, base.VisitChildren).Match(
				x => (CallState?)null,
				x => x.Value);
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
		{
			return new CallState(null, context.Depth(), [context.GetText()]);
		}

		/// <summary>
		/// Visit a parse tree produced by <see cref="SharpMUSHParser.eqsplitCommandArgs"/>.
		/// <para>
		/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
		/// on <paramref name="context"/>.
		/// </para>
		/// </summary>
		/// <param name="context">The parse tree.</param>
		/// <return>The visitor result.</return>
		public override CallState? VisitEqsplitCommandArgs([NotNull] SharpMUSHParser.EqsplitCommandArgsContext context)
		{
			var baseArg = base.VisitChildren(context.singleCommandArg());
			var commaArgs = base.VisitChildren(context.commaCommandArgs());
			return new CallState(null, context.Depth(), [baseArg!.Message!.ToString(), ..commaArgs!.Arguments]);
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
		public override CallState? VisitEqsplitCommand([NotNull] SharpMUSHParser.EqsplitCommandContext context)
		{
			var baseArg = base.VisitChildren(context.singleCommandArg()[0]);
			var RSArg = base.VisitChildren(context.singleCommandArg()[1]);
			return new CallState(null, context.Depth(), [baseArg!.Message!.ToString(), .. RSArg!.Arguments]);
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
			return new CallState(null, context.Depth(), children!.Arguments);
		}
	}
}