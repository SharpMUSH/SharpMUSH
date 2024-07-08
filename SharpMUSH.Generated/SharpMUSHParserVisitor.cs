//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     ANTLR Version: 4.13.1
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

// Generated from D:/SharpMUSH/SharpMUSH.Generated/SharpMUSHParser.g4 by ANTLR 4.13.1

// Unreachable code detected
#pragma warning disable 0162
// The variable '...' is assigned but its value is never used
#pragma warning disable 0219
// Missing XML comment for publicly visible type or member '...'
#pragma warning disable 1591
// Ambiguous reference in cref attribute
#pragma warning disable 419

using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using IToken = Antlr4.Runtime.IToken;

/// <summary>
/// This interface defines a complete generic visitor for a parse tree produced
/// by <see cref="SharpMUSHParser"/>.
/// </summary>
/// <typeparam name="Result">The return type of the visit operation.</typeparam>
[System.CodeDom.Compiler.GeneratedCode("ANTLR", "4.13.1")]
[System.CLSCompliant(false)]
public interface ISharpMUSHParserVisitor<Result> : IParseTreeVisitor<Result> {
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.singleCommandString"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitSingleCommandString([NotNull] SharpMUSHParser.SingleCommandStringContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.commandString"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitCommandString([NotNull] SharpMUSHParser.CommandStringContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.commandList"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitCommandList([NotNull] SharpMUSHParser.CommandListContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.command"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitCommand([NotNull] SharpMUSHParser.CommandContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.firstCommandMatch"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitFirstCommandMatch([NotNull] SharpMUSHParser.FirstCommandMatchContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.eqsplitCommandArgs"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitEqsplitCommandArgs([NotNull] SharpMUSHParser.EqsplitCommandArgsContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.eqsplitCommand"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitEqsplitCommand([NotNull] SharpMUSHParser.EqsplitCommandContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.commaCommandArgs"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitCommaCommandArgs([NotNull] SharpMUSHParser.CommaCommandArgsContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.singleCommandArg"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitSingleCommandArg([NotNull] SharpMUSHParser.SingleCommandArgContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.plainString"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitPlainString([NotNull] SharpMUSHParser.PlainStringContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.evaluationString"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitEvaluationString([NotNull] SharpMUSHParser.EvaluationStringContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.explicitEvaluationString"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitExplicitEvaluationString([NotNull] SharpMUSHParser.ExplicitEvaluationStringContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.explicitEvaluationStringSubstitution"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitExplicitEvaluationStringSubstitution([NotNull] SharpMUSHParser.ExplicitEvaluationStringSubstitutionContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.explicitEvaluationStringFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitExplicitEvaluationStringFunction([NotNull] SharpMUSHParser.ExplicitEvaluationStringFunctionContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.explicitEvaluationText"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitExplicitEvaluationText([NotNull] SharpMUSHParser.ExplicitEvaluationTextContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.funName"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitFunName([NotNull] SharpMUSHParser.FunNameContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.function"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitFunction([NotNull] SharpMUSHParser.FunctionContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.funArguments"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitFunArguments([NotNull] SharpMUSHParser.FunArgumentsContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.validSubstitution"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitValidSubstitution([NotNull] SharpMUSHParser.ValidSubstitutionContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.complexSubstitutionSymbol"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitComplexSubstitutionSymbol([NotNull] SharpMUSHParser.ComplexSubstitutionSymbolContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.substitutionSymbol"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitSubstitutionSymbol([NotNull] SharpMUSHParser.SubstitutionSymbolContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.genericText"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitGenericText([NotNull] SharpMUSHParser.GenericTextContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.escapedText"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitEscapedText([NotNull] SharpMUSHParser.EscapedTextContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.ansi"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitAnsi([NotNull] SharpMUSHParser.AnsiContext context);
}
