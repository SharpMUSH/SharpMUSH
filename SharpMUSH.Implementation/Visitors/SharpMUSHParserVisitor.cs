using Antlr4.Runtime.Misc;
using Serilog;
using SharpMUSH.Library.ParserInterfaces;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// This class implements the SharpMUSHParserBaseVisitor from the Generated code.
/// If additional pieces of the parse-tree are added, the Generated project must be re-generated 
/// and new Visitors may need to be added.
/// </summary>
/// <param name="parser">The Parser, so that inner functions can force a parser-call.</param>
/// <param name="source">The original MarkupString. A plain GetText is not good enough to get the proper value back.</param>
public class SharpMUSHParserVisitor(IMUSHCodeParser parser, MString source)
	: SharpMUSHParserBaseVisitor<ValueTask<CallState?>>
{
	protected override ValueTask<CallState?> DefaultResult => ValueTask.FromResult(default(CallState?));

	protected override async ValueTask<CallState?> AggregateResult(ValueTask<CallState?> aggregate,
		ValueTask<CallState?> nextResult)
	{
		var agg = await aggregate;
		var next = await nextResult;
		if (agg?.Arguments != null || next?.Arguments != null)
		{
			return (agg ?? next!) with
			{
				Arguments =
				[
					.. agg?.Arguments ?? Enumerable.Empty<MString>(),
					.. next?.Arguments ?? Enumerable.Empty<MString>()
				]
			};
		}

		if (agg?.Message != null && next?.Message != null)
		{
			return agg with { Message = MModule.concat(agg.Message, next.Message) };
		}

		return agg ?? next ?? null;
	}

	public async override ValueTask<CallState?> VisitFunction([NotNull] SharpMUSHParser.FunctionContext context)
	{
		var functionName = context.funName().GetText().TrimEnd()[..^1];
		var arguments = context.funArguments()?.funArgument() ?? Enumerable.Empty<FunArgumentContext>().ToArray();

		Log.Logger.Information("VisitFunction: Fun: {Text}, Args: {Args}", functionName,
			arguments?.Select(x => x.GetText()));
		return await Functions.Functions.CallFunction(functionName.ToLower(), source, parser, context, arguments!, this);
	}

	public override async ValueTask<CallState?> VisitEvaluationString(
		[NotNull] SharpMUSHParser.EvaluationStringContext context)
	{
		if (parser.CurrentState.ParseMode.HasFlag(ParseMode.NoParse))
			return new(
				MModule.substring(context.Start.StartIndex,
					context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source),
				context.Depth());

		return await base.VisitChildren(context)
		       ?? new(
			       MModule.substring(context.Start.StartIndex,
				       context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source),
			       context.Depth());
	}

	public override async ValueTask<CallState?> VisitExplicitEvaluationString(
		[NotNull] SharpMUSHParser.ExplicitEvaluationStringContext context)
	{
		if (parser.CurrentState.ParseMode.HasFlag(ParseMode.NoParse))
			return new(
				MModule.substring(context.Start.StartIndex,
					context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source),
				context.Depth());

		return await base.VisitChildren(context)
		       ?? new(
			       MModule.substring(context.Start.StartIndex,
				       context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source),
			       context.Depth());
	}

	public override async ValueTask<CallState?> VisitGenericText([NotNull] SharpMUSHParser.GenericTextContext context)
		=> await base.VisitChildren(context)
		   ?? new(
			   MModule.substring(context.Start.StartIndex,
				   context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source),
			   context.Depth());

	public override async ValueTask<CallState?> VisitBeginGenericText(
		[NotNull] SharpMUSHParser.BeginGenericTextContext context)
		=> await base.VisitChildren(context)
		   ?? new(
			   MModule.substring(context.Start.StartIndex,
				   context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source),
			   context.Depth());

	public override async ValueTask<CallState?> VisitValidSubstitution(
		[NotNull] SharpMUSHParser.ValidSubstitutionContext context)
	{
		var textContents = MModule.single(context.GetText());
		var complexSubstitutionSymbol = context.complexSubstitutionSymbol();
		var simpleSubstitutionSymbol = context.substitutionSymbol();

		if (complexSubstitutionSymbol != null)
		{
			var state = await VisitChildren(context);
			return Substitutions.Substitutions.ParseComplexSubstitution(state, parser, complexSubstitutionSymbol);
		}
		else if (simpleSubstitutionSymbol != null)
		{
			return Substitutions.Substitutions.ParseSimpleSubstitution(simpleSubstitutionSymbol.GetText(), parser,
				simpleSubstitutionSymbol);
		}
		else
		{
			return await base.VisitChildren(context)
			       ?? new(textContents, context.Depth());
		}
	}

	public override async ValueTask<CallState?> VisitCommand([NotNull] SharpMUSHParser.CommandContext context)
	{
		var text = MModule.substring(context.Start.StartIndex,
			context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source);
		Log.Logger.Information("VisitCommand: {Text}", text);

		return (await Commands.Commands.EvaluateCommands(parser, source, context, base.VisitChildren)).Match(
			x => (CallState?)null,
			x => x.Value);
	}

	public override async ValueTask<CallState?> VisitStartCommandString(
		[NotNull] SharpMUSHParser.StartCommandStringContext context)
	{
		var text = MModule.substring(context.Start.StartIndex,
			context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source);
		Log.Logger.Information("VisitCommandString: {Text}", text);
		return await base.VisitChildren(context)
		       ?? new CallState(text, context.Depth());
	}

	public override async ValueTask<CallState?> VisitCommandList([NotNull] SharpMUSHParser.CommandListContext context)
	{
		var text = MModule.substring(context.Start.StartIndex,
			context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source);
		Log.Logger.Information("VisitCommandList: {Text}", text);
		return await base.VisitChildren(context)
		       ?? new CallState(text, context.Depth());
	}

	public override async ValueTask<CallState?> VisitStartSingleCommandString(
		[NotNull] SharpMUSHParser.StartSingleCommandStringContext context)
	{
		var text = MModule.substring(context.Start.StartIndex,
			context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source);
		Log.Logger.Information("VisitSingleCommandString: {Text}", text);
		return await base.VisitChildren(context) ?? new CallState(text, context.Depth());
	}

	public override async ValueTask<CallState?> VisitEscapedText([NotNull] SharpMUSHParser.EscapedTextContext context)
		=> await base.VisitChildren(context)
		   ?? new(
			   MModule.substring(context.Start.StartIndex + 1, context.Stop.StopIndex - context.Start.StartIndex + 1 - 1,
				   source), context.Depth());

	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.singleCommandArg"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public override ValueTask<CallState?> VisitSingleCommandArg([NotNull] SharpMUSHParser.SingleCommandArgContext context)
		=> ValueTask.FromResult<CallState?>(new(
			null,
			context.Depth(),
			[
				MModule.substring(context.Start.StartIndex,
					context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source)
			],
			() => Task.FromResult<MString?>(null)));

	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.eqsplitCommandArgs"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public override async ValueTask<CallState?> VisitStartEqSplitCommandArgs(
		[NotNull] SharpMUSHParser.StartEqSplitCommandArgsContext context)
	{
		var baseArg = await base.VisitChildren(context.singleCommandArg());
		var commaArgs = await base.VisitChildren(context.commaCommandArgs());
		Log.Logger.Information("VisitEqsplitCommandArgs: C1: {Text} - C2: {Text2}", baseArg?.ToString(),
			commaArgs?.ToString());
		return new(null, context.Depth(), [baseArg!.Message!, .. commaArgs?.Arguments ?? []],
			() => Task.FromResult<MString?>(null));
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
	public override async ValueTask<CallState?> VisitStartEqSplitCommand(
		[NotNull] SharpMUSHParser.StartEqSplitCommandContext context)
	{
		var singleCommandArg = context.singleCommandArg();
		var baseArg = await base.VisitChildren(singleCommandArg[0]);
		var rsArg = singleCommandArg.Length > 1 ? await base.VisitChildren(singleCommandArg[1]) : null;
		Log.Logger.Information("VisitEqSplitCommand: C1: {Text} - C2: {Text2}", baseArg?.ToString(), rsArg?.ToString());
		return new CallState(
			null,
			context.Depth(),
			[baseArg!.Message!, rsArg?.Message ?? MModule.empty() ],
			() => Task.FromResult<MString?>(null));
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
	public override async ValueTask<CallState?> VisitCommaCommandArgs(
		[NotNull] SharpMUSHParser.CommaCommandArgsContext context)
	{
		var children = await base.VisitChildren(context);
		return new(null, context.Depth(), children!.Arguments, () => Task.FromResult<MString?>(null));
	}

	public override async ValueTask<CallState?> VisitComplexSubstitutionSymbol(
		[NotNull] ComplexSubstitutionSymbolContext context)
	{
		if (context.ChildCount > 1)
			return await base.VisitChildren(context);
		else if (context.REG_NUM() is not null)
			return new(
				MModule.substring(context.Start.StartIndex + 1, context.Stop.StopIndex - context.Start.StartIndex + 1 - 1,
					source), context.Depth());
		else if (context.ITEXT_NUM() is not null || context.STEXT_NUM() is not null)
			return new(
				MModule.substring(context.Start.StartIndex + 1, context.Stop.StopIndex - context.Start.StartIndex + 1 - 1,
					source), context.Depth());
		else
			return new(
				MModule.substring(context.Start.StartIndex,
					context.Stop?.StopIndex == null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source),
				context.Depth());
	}
}