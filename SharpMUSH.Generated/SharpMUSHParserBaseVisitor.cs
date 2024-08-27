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
using ParserRuleContext = Antlr4.Runtime.ParserRuleContext;

/// <summary>
/// This class provides an empty implementation of <see cref="ISharpMUSHParserVisitor{Result}"/>,
/// which can be extended to create a visitor which only needs to handle a subset
/// of the available methods.
/// </summary>
/// <typeparam name="Result">The return type of the visit operation.</typeparam>
[System.CodeDom.Compiler.GeneratedCode("ANTLR", "4.13.1")]
[System.Diagnostics.DebuggerNonUserCode]
[System.CLSCompliant(false)]
public partial class SharpMUSHParserBaseVisitor<Result> : AbstractParseTreeVisitor<Result>, ISharpMUSHParserVisitor<Result> {
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.startSingleCommandString"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitStartSingleCommandString([NotNull] SharpMUSHParser.StartSingleCommandStringContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.startCommandString"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitStartCommandString([NotNull] SharpMUSHParser.StartCommandStringContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.startPlainCommaCommandArgs"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitStartPlainCommaCommandArgs([NotNull] SharpMUSHParser.StartPlainCommaCommandArgsContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.startEqSplitCommandArgs"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitStartEqSplitCommandArgs([NotNull] SharpMUSHParser.StartEqSplitCommandArgsContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.startEqSplitCommand"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitStartEqSplitCommand([NotNull] SharpMUSHParser.StartEqSplitCommandContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.startPlainSingleCommandArg"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitStartPlainSingleCommandArg([NotNull] SharpMUSHParser.StartPlainSingleCommandArgContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.startPlainString"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitStartPlainString([NotNull] SharpMUSHParser.StartPlainStringContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.commandList"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitCommandList([NotNull] SharpMUSHParser.CommandListContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.command"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitCommand([NotNull] SharpMUSHParser.CommandContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.firstCommandMatch"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitFirstCommandMatch([NotNull] SharpMUSHParser.FirstCommandMatchContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.commaCommandArgs"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitCommaCommandArgs([NotNull] SharpMUSHParser.CommaCommandArgsContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.singleCommandArg"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitSingleCommandArg([NotNull] SharpMUSHParser.SingleCommandArgContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.evaluationString"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitEvaluationString([NotNull] SharpMUSHParser.EvaluationStringContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.explicitEvaluationString"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitExplicitEvaluationString([NotNull] SharpMUSHParser.ExplicitEvaluationStringContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.explicitEvaluationStringConcatenatedRepeat"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitExplicitEvaluationStringConcatenatedRepeat([NotNull] SharpMUSHParser.ExplicitEvaluationStringConcatenatedRepeatContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.funName"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitFunName([NotNull] SharpMUSHParser.FunNameContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.function"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitFunction([NotNull] SharpMUSHParser.FunctionContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.funArguments"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitFunArguments([NotNull] SharpMUSHParser.FunArgumentsContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.validSubstitution"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitValidSubstitution([NotNull] SharpMUSHParser.ValidSubstitutionContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.complexSubstitutionSymbol"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitComplexSubstitutionSymbol([NotNull] SharpMUSHParser.ComplexSubstitutionSymbolContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.substitutionSymbol"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitSubstitutionSymbol([NotNull] SharpMUSHParser.SubstitutionSymbolContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.genericText"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitGenericText([NotNull] SharpMUSHParser.GenericTextContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.beginGenericText"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitBeginGenericText([NotNull] SharpMUSHParser.BeginGenericTextContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.escapedText"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitEscapedText([NotNull] SharpMUSHParser.EscapedTextContext context) { return VisitChildren(context); }
	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.ansi"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public virtual Result VisitAnsi([NotNull] SharpMUSHParser.AnsiContext context) { return VisitChildren(context); }
}
