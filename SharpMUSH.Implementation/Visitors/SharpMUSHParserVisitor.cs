﻿using System.Runtime.CompilerServices;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using Microsoft.Extensions.Logging;
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
public class SharpMUSHParserVisitor(ILogger logger, IMUSHCodeParser parser, MString source)
	: SharpMUSHParserBaseVisitor<ValueTask<CallState?>>
{
	private int _braceDepthCounter;

	protected override ValueTask<CallState?> DefaultResult => ValueTask.FromResult<CallState?>(null);

	public override async ValueTask<CallState?> Visit(IParseTree tree) => await tree.Accept(this);

	public override async ValueTask<CallState?> VisitChildren(IRuleNode node)
	{
		var result = await DefaultResult;
		
		foreach (var child in Enumerable
			               .Range(0, node.ChildCount)
			               .Select(node.GetChild))
		{
			result = AggregateResult(result, await child.Accept(this));
		}
		
		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining|MethodImplOptions.AggressiveOptimization)]
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
		if (parser.CurrentState.ParseMode is ParseMode.NoParse or ParseMode.NoEval)
		{
			// var a = await VisitChildren(context);
			return new CallState(context.GetText());
		}

		var functionName = context.FUNCHAR().GetText().TrimEnd()[..^1];
		var arguments = context.evaluationString() ?? Enumerable.Empty<EvaluationStringContext>().ToArray();

		/* await parser.NotifyService.Notify(parser.CurrentState.Executor!.Value, MModule.single(
			$"#{parser.CurrentState.Caller!.Value.Number}! {new string(' ', parser.CurrentState.ParserFunctionDepth!.Value)}{context.GetText()} :")); */

		var result =
			await Functions.Functions.CallFunction(logger, functionName.ToLower(), source, parser, context, arguments, this);

		/* await parser.NotifyService.Notify(parser.CurrentState.Caller!.Value, MModule.single(
			$"#{parser.CurrentState.Caller!.Value.Number}! {new string(' ', parser.CurrentState.ParserFunctionDepth!.Value)}{context.GetText()} => {result.Message}")); */

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
		/* var isGenericText = context.beginGenericText() is not null;

		if (!isGenericText)
		{
			await parser.NotifyService.Notify(parser.CurrentState.Executor!.Value, MModule.single(
				$"#{parser.CurrentState.Caller!.Value.Number}! {new string(' ', parser.CurrentState.ParserFunctionDepth!.Value)}{context.GetText()} :"));
		} */

		return await VisitChildren(context)
		       ?? new CallState(
			       MModule.substring(context.Start.StartIndex,
				       context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1),
				       source),
			       context.Depth());

		/* if (!isGenericText)
		{
			await parser.NotifyService.Notify(parser.CurrentState.Executor!.Value, MModule.single(
				$"#{parser.CurrentState.Caller!.Value.Number}! {new string(' ', parser.CurrentState.ParserFunctionDepth!.Value)}{context.GetText()} => {result.Message}"));
		} */
	}

	public override async ValueTask<CallState?> VisitBracePattern(
		[NotNull] BracePatternContext context)
	{
		_braceDepthCounter++;

		CallState? result;
		var vc = await VisitChildren(context);

		if (_braceDepthCounter <= 1)
		{
			result = vc ?? new CallState(
				MModule.substring(context.Start.StartIndex,
					context.Stop?.StopIndex is null
						? 0
						: context.Stop.StopIndex - context.Start.StartIndex + 1, source),
				context.Depth());
		}
		else
		{
			result = vc is not null
				? vc with
				{
					Message = MModule.multiple([
						MModule.single("{"),
						vc.Message,
						MModule.single("}")
					])
				}
				: new CallState(
					MModule.substring(context.Start.StartIndex,
						context.Stop?.StopIndex is null
							? 0
							: context.Stop.StopIndex - context.Start.StartIndex + 1, source),
					context.Depth());
		}

		_braceDepthCounter--;
		return result;
	}

	public override async ValueTask<CallState?> VisitBracketPattern(
		[NotNull] BracketPatternContext context)
	{
		if (parser.CurrentState.ParseMode is not ParseMode.NoParse and not ParseMode.NoEval)
		{
			/*
			await parser.NotifyService.Notify(parser.CurrentState.Caller!.Value,
				$"#{parser.CurrentState.Caller!.Value.Number}! {new string(' ', parser.CurrentState.ParserFunctionDepth!.Value)}{text} :");
			*/

			var resultQ = await VisitChildren(context)
			              ?? new CallState(
				              MModule.substring(context.Start.StartIndex,
					              context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1),
					              source),
				              context.Depth());


			return resultQ;
		}

		var result = await VisitChildren(context);
		if (result == null)
		{
			return new CallState(
				MModule.substring(context.Start.StartIndex,
					context.Stop?.StopIndex is null
						? 0
						: context.Stop.StopIndex - context.Start.StartIndex + 1, source),
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
		   ?? new CallState(
			   MModule.substring(context.Start.StartIndex,
				   context.Stop?.StopIndex is null
					   ? 0
					   : context.Stop.StopIndex - context.Start.StartIndex + 1,
				   source),
			   context.Depth());

	public override async ValueTask<CallState?> VisitValidSubstitution(
		[NotNull] ValidSubstitutionContext context)
	{
		if (parser.CurrentState.ParseMode is ParseMode.NoParse or ParseMode.NoEval)
		{
			// TODO: This does not work in the case of a QREG with an evaluationstring in it.
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

		return await VisitChildren(context) ?? new CallState(textContents, context.Depth());
	}

	public override async ValueTask<CallState?> VisitCommand([NotNull] CommandContext context)
	{
		if (parser.CurrentState.ParseMode == ParseMode.NoParse)
		{
			return await VisitChildren(context) ?? new CallState(context.GetText());
		}

		return (await Commands.Commands.EvaluateCommands(logger, parser, source, context, VisitChildren))
			.Match<CallState?>(
				x => x,
				_ => null);
	}

	public override async ValueTask<CallState?> VisitStartCommandString(
		[NotNull] StartCommandStringContext context)
	{
		var result = await VisitChildren(context);
		if (result != null)
		{
			return result;
		}

		var text = MModule.substring(
			context.Start.StartIndex,
			context.Stop?.StopIndex is null
				? 0
				: context.Stop.StopIndex - context.Start.StartIndex + 1,
			source);
		return new CallState(text, context.Depth());
	}

	public override async ValueTask<CallState?> VisitCommandList([NotNull] CommandListContext context)
	{
		var result = await VisitChildren(context);
		if (result != null)
		{
			return result;
		}

		var text = MModule.substring(context.Start.StartIndex,
			context.Stop?.StopIndex is null
				? 0
				: context.Stop.StopIndex - context.Start.StartIndex + 1,
			source);
		return new CallState(text, context.Depth());
	}

	public override async ValueTask<CallState?> VisitStartSingleCommandString(
		[NotNull] StartSingleCommandStringContext context)
	{
		var result = await VisitChildren(context);
		if (result != null)
		{
			return result;
		}

		var text = MModule.substring(context.Start.StartIndex,
			context.Stop?.StopIndex is null
				? 0
				: context.Stop.StopIndex - context.Start.StartIndex + 1,
			source);
		return new CallState(text, context.Depth());
	}

	public override async ValueTask<CallState?> VisitEscapedText([NotNull] EscapedTextContext context)
		=> await VisitChildren(context)
		   ?? new CallState(
			   MModule.substring(
				   context.Start.StartIndex + 1,
				   context.Stop.StopIndex - context.Start.StartIndex + 1 - 1,
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
	public override async ValueTask<CallState?> VisitSingleCommandArg([NotNull] SingleCommandArgContext context)
	{
		var visitedChildren = await VisitChildren(context);

		return new CallState(
			Message: null,
			context.Depth(),
			Arguments:
			[
				visitedChildren?.Message ??
				MModule.substring(context.Start.StartIndex,
					context.Stop?.StopIndex is null
						? 0
						: context.Stop.StopIndex - context.Start.StartIndex + 1, source)
			],
			ParsedMessage: () => Task.FromResult<MString?>(null));
	}

	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.startEqSplitCommandArgs"/>.
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
		return new CallState(null,
			context.Depth(),
			[baseArg!.Message!, .. commaArgs?.Arguments ?? []],
			() => Task.FromResult<MString?>(null));
	}

	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.startEqSplitCommand"/>.
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
		[NotNull] CommaCommandArgsContext context) =>
		new(
			null,
			context.Depth(),
			(await VisitChildren(context))!.Arguments,
			() => Task.FromResult<MString?>(null));

	public override async ValueTask<CallState?> VisitComplexSubstitutionSymbol(
		[NotNull] ComplexSubstitutionSymbolContext context)
	{
		if (context.ChildCount > 1)
			return await VisitChildren(context);

		if (context.REG_NUM() is not null)
			return new CallState(
				MModule.substring(context.Start.StartIndex + 1, context.Stop.StopIndex - context.Start.StartIndex + 1 - 1,
					source), context.Depth());

		if (context.ITEXT_NUM() is not null || context.STEXT_NUM() is not null)
			return new CallState(
				MModule.substring(context.Start.StartIndex + 1, context.Stop.StopIndex - context.Start.StartIndex + 1 - 1,
					source), context.Depth());

		return new CallState(
			MModule.substring(context.Start.StartIndex,
				context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source),
			context.Depth());
	}
}