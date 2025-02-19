using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
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
	protected override ValueTask<CallState?> DefaultResult => ValueTask.FromResult<CallState?>(null);

	public override async ValueTask<CallState?> Visit(IParseTree tree) => await tree.Accept(this);

	public override async ValueTask<CallState?> VisitChildren(IRuleNode node)
	{
		var result = await DefaultResult;
		var childCount = node.ChildCount;
		for (var i = 0; i < childCount /* && *ShouldVisitNextChild(node, result) */; ++i)
		{
			var nextResult = await node.GetChild(i).Accept(this);
			result = AggregateResult(result, nextResult);
		}
		return result;
	}

	protected override async ValueTask<CallState?> AggregateResult(ValueTask<CallState?> aggregate,
		ValueTask<CallState?> nextResult)
		=> (await aggregate, await nextResult) switch
		{
			(null, null)
				=> null,
			({ Arguments: not null } agg, { Arguments: not null } next)
				=> agg with { Arguments = [.. agg.Arguments, .. next.Arguments] },
			({ Message: not null } agg, { Message: not null } next)
				=> agg with { Message = MModule.concat(agg.Message, next.Message) },
			var (agg, next)
				=> agg ?? next
		};
	
	private static CallState? AggregateResult(CallState? aggregate,
		CallState? nextResult)
		=> (aggregate, nextResult) switch
		{
			(null, null)
				=> null,
			({ Arguments: not null } agg, { Arguments: not null } next)
				=> agg with { Arguments = [.. agg.Arguments, .. next.Arguments] },
			({ Message: not null } agg, { Message: not null } next)
				=> agg with { Message = MModule.concat(agg.Message, next.Message) },
			var (agg, next)
				=> agg ?? next
		};

	public override async ValueTask<CallState?> VisitFunction([NotNull] FunctionContext context)
	{
		if (parser.CurrentState.ParseMode == ParseMode.NoParse)
		{
			return new CallState(context.GetText());
		}

		var functionName = context.funName().GetText().TrimEnd()[..^1];
		var arguments = context.funArguments()?.funArgument() ?? Enumerable.Empty<FunArgumentContext>().ToArray();

		await parser.NotifyService.Notify(parser.CurrentState.Executor!.Value, MModule.single(
			$"#{parser.CurrentState.Caller!.Value.Number}! {new string(' ', parser.CurrentState.ParserFunctionDepth!.Value)}{context.GetText()} :"));

		var result =
			await Functions.Functions.CallFunction(functionName.ToLower(), source, parser, context, arguments!, this);

		await parser.NotifyService.Notify(parser.CurrentState.Caller!.Value, MModule.single(
			$"#{parser.CurrentState.Caller!.Value.Number}! {new string(' ', parser.CurrentState.ParserFunctionDepth!.Value)}{context.GetText()} => {result.Message}"));

		return result;
	}

	public override async ValueTask<CallState?> VisitEvaluationString(
		[NotNull] EvaluationStringContext context) => await VisitChildren(context) ?? new(
		MModule.substring(context.Start.StartIndex,
			context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source),
		context.Depth());

	public override async ValueTask<CallState?> VisitExplicitEvaluationString(
		[NotNull] ExplicitEvaluationStringContext context)
	{
		var isGenericText = context.beginGenericText() is not null;

		if (!isGenericText)
		{
			await parser.NotifyService.Notify(parser.CurrentState.Executor!.Value, MModule.single(
				$"#{parser.CurrentState.Caller!.Value.Number}! {new string(' ', parser.CurrentState.ParserFunctionDepth!.Value)}{context.GetText()} :"));
		}

		var result = await VisitChildren(context)
		             ?? new(
			             MModule.substring(context.Start.StartIndex,
				             context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1),
				             source),
			             context.Depth());

		if (!isGenericText)
		{
			await parser.NotifyService.Notify(parser.CurrentState.Executor!.Value, MModule.single(
				$"#{parser.CurrentState.Caller!.Value.Number}! {new string(' ', parser.CurrentState.ParserFunctionDepth!.Value)}{context.GetText()} => {result.Message}"));
		}

		return result;
	}

	public override async ValueTask<CallState?> VisitExplicitEvaluationStringConcatenatedRepeat(
		[NotNull] ExplicitEvaluationStringConcatenatedRepeatContext context) =>
		await VisitChildren(context)
		?? new(
			MModule.substring(context.Start.StartIndex,
				context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source),
			context.Depth());

	public override async ValueTask<CallState?> VisitBracePattern(
		[NotNull] BracePatternContext context) =>
		await VisitChildren(context)
		?? new(
			MModule.substring(context.Start.StartIndex,
				context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source),
			context.Depth());

	public override async ValueTask<CallState?> VisitBracketPattern(
		[NotNull] BracketPatternContext context)
	{
		if (parser.CurrentState.ParseMode != ParseMode.NoParse)
		{
			var text = context.GetText();

			await parser.NotifyService.Notify(parser.CurrentState.Caller!.Value,
				$"#{parser.CurrentState.Caller!.Value.Number}! {new string(' ', parser.CurrentState.ParserFunctionDepth!.Value)}{text} :");

			var resultQ = await VisitChildren(context)
			              ?? new(
				              MModule.substring(context.Start.StartIndex,
					              context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1),
					              source),
				              context.Depth());


			return resultQ;
		}

		var result = await VisitChildren(context);
		if (result == null)
		{
			return new(
				MModule.substring(context.Start.StartIndex,
					context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source),
				context.Depth());
		}

		return result with
		{
			Message = MModule.multiple([
				MModule.single("["),
				result.Message,
				MModule.single("]")
			])
		};
	}

	public override async ValueTask<CallState?> VisitGenericText([NotNull] GenericTextContext context)
		=> await VisitChildren(context)
		   ?? new(
			   MModule.substring(context.Start.StartIndex,
				   context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source),
			   context.Depth());

	public override async ValueTask<CallState?> VisitBeginGenericText(
		[NotNull] BeginGenericTextContext context)
		=> await VisitChildren(context)
		   ?? new(
			   MModule.substring(context.Start.StartIndex,
				   context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source),
			   context.Depth());

	public override async ValueTask<CallState?> VisitValidSubstitution(
		[NotNull] ValidSubstitutionContext context)
	{
		if (parser.CurrentState.ParseMode == ParseMode.NoParse)
		{
			return new CallState("%" + context.GetText());
		}

		var textContents = MModule.single(context.GetText());
		var complexSubstitutionSymbol = context.complexSubstitutionSymbol();
		var simpleSubstitutionSymbol = context.substitutionSymbol();

		if (complexSubstitutionSymbol is not null)
		{
			var state = await VisitChildren(context);
			return await Substitutions.Substitutions.ParseComplexSubstitution(state, parser, complexSubstitutionSymbol);
		}

		if (simpleSubstitutionSymbol is not null)
		{
			return await Substitutions.Substitutions.ParseSimpleSubstitution(simpleSubstitutionSymbol.GetText(), parser,
				simpleSubstitutionSymbol);
		}

		return await VisitChildren(context) ?? new(textContents, context.Depth());
	}

	public override async ValueTask<CallState?> VisitCommand([NotNull] CommandContext context)
	{
		var text = MModule.substring(context.Start.StartIndex,
			context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source);
		// Log.Logger.Information("VisitCommand: {Text}", text);

		return (await Commands.Commands.EvaluateCommands(parser, source, context, VisitChildren))
			.Match(
				x => x,
				_ => null as CallState);
	}

	public override async ValueTask<CallState?> VisitStartCommandString(
		[NotNull] StartCommandStringContext context)
	{
		var text = MModule.substring(context.Start.StartIndex,
			context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source);
		// Log.Logger.Information("VisitCommandString: {Text}", text);
		return await VisitChildren(context)
		       ?? new CallState(text, context.Depth());
	}

	public override async ValueTask<CallState?> VisitCommandList([NotNull] CommandListContext context)
	{
		var text = MModule.substring(context.Start.StartIndex,
			context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source);
		// Log.Logger.Information("VisitCommandList: {Text}", text);

		return await VisitChildren(context)
		       ?? new CallState(text, context.Depth());
	}

	public override async ValueTask<CallState?> VisitStartSingleCommandString(
		[NotNull] StartSingleCommandStringContext context)
	{
		var text = MModule.substring(context.Start.StartIndex,
			context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source);
		// Log.Logger.Information("VisitSingleCommandString: {Text}", text);
		return await VisitChildren(context) ?? new CallState(text, context.Depth());
	}

	public override async ValueTask<CallState?> VisitEscapedText([NotNull] EscapedTextContext context)
		=> await VisitChildren(context)
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
	public override ValueTask<CallState?> VisitSingleCommandArg([NotNull] SingleCommandArgContext context)
		=> ValueTask.FromResult<CallState?>(new(
			null,
			context.Depth(),
			[
				MModule.substring(context.Start.StartIndex,
					context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source)
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
		[NotNull] StartEqSplitCommandArgsContext context)
	{
		var baseArg = await VisitChildren(context.singleCommandArg());
		var commaArgs = await VisitChildren(context.commaCommandArgs());
		// Log.Logger.Information("VisitEqsplitCommandArgs: C1: {Text} - C2: {Text2}", baseArg?.ToString(), commaArgs?.ToString());
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
		[NotNull] StartEqSplitCommandContext context)
	{
		var singleCommandArg = context.singleCommandArg();
		var baseArg = await VisitChildren(singleCommandArg[0]);
		var rsArg = singleCommandArg.Length > 1 ? await VisitChildren(singleCommandArg[1]) : null;
		// Log.Logger.Information("VisitEqSplitCommand: C1: {Text} - C2: {Text2}", baseArg?.ToString(), rsArg?.ToString());
		return new CallState(
			null,
			context.Depth(),
			[baseArg!.Message!, rsArg?.Message ?? MModule.empty()],
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
		[NotNull] CommaCommandArgsContext context)
	{
		var children = await VisitChildren(context);
		return new(null, context.Depth(), children!.Arguments,
			() => Task.FromResult<MString?>(null));
	}

	public override async ValueTask<CallState?> VisitComplexSubstitutionSymbol(
		[NotNull] ComplexSubstitutionSymbolContext context)
	{
		if (context.ChildCount > 1)
			return await VisitChildren(context);

		if (context.REG_NUM() is not null)
			return new(
				MModule.substring(context.Start.StartIndex + 1, context.Stop.StopIndex - context.Start.StartIndex + 1 - 1,
					source), context.Depth());

		if (context.ITEXT_NUM() is not null || context.STEXT_NUM() is not null)
			return new(
				MModule.substring(context.Start.StartIndex + 1, context.Stop.StopIndex - context.Start.StartIndex + 1 - 1,
					source), context.Depth());

		return new(
			MModule.substring(context.Start.StartIndex,
				context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source),
			context.Depth());
	}
}