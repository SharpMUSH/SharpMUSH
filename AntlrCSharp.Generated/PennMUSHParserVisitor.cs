//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     ANTLR Version: 4.13.1
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

// Generated from c:/Users/admin/OneDrive/Documents/Repos/MUParser/AntlrCSharp/PennMUSHParser.g4 by ANTLR 4.13.1

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
/// by <see cref="PennMUSHParser"/>.
/// </summary>
/// <typeparam name="Result">The return type of the visit operation.</typeparam>
[System.CodeDom.Compiler.GeneratedCode("ANTLR", "4.13.1")]
[System.CLSCompliant(false)]
public interface IPennMUSHParserVisitor<Result> : IParseTreeVisitor<Result> {
	/// <summary>
	/// Visit a parse tree produced by <see cref="PennMUSHParser.evaluationString"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitEvaluationString([NotNull] PennMUSHParser.EvaluationStringContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="PennMUSHParser.explicitEvaluationString"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitExplicitEvaluationString([NotNull] PennMUSHParser.ExplicitEvaluationStringContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="PennMUSHParser.explicitFunction"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitExplicitFunction([NotNull] PennMUSHParser.ExplicitFunctionContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="PennMUSHParser.function"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitFunction([NotNull] PennMUSHParser.FunctionContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="PennMUSHParser.funArguments"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitFunArguments([NotNull] PennMUSHParser.FunArgumentsContext context);
	/// <summary>
	/// Visit a parse tree produced by <see cref="PennMUSHParser.genericText"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	Result VisitGenericText([NotNull] PennMUSHParser.GenericTextContext context);
}
