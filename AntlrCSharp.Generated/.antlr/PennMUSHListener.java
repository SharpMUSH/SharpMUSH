// Generated from c:/Users/admin/OneDrive/Documents/Repos/MUParser/AntlrCSharp/PennMUSH.g4 by ANTLR 4.13.1
import org.antlr.v4.runtime.tree.ParseTreeListener;

/**
 * This interface defines a complete listener for a parse tree produced by
 * {@link PennMUSHParser}.
 */
public interface PennMUSHListener extends ParseTreeListener {
	/**
	 * Enter a parse tree produced by {@link PennMUSHParser#evaluationString}.
	 * @param ctx the parse tree
	 */
	void enterEvaluationString(PennMUSHParser.EvaluationStringContext ctx);
	/**
	 * Exit a parse tree produced by {@link PennMUSHParser#evaluationString}.
	 * @param ctx the parse tree
	 */
	void exitEvaluationString(PennMUSHParser.EvaluationStringContext ctx);
	/**
	 * Enter a parse tree produced by {@link PennMUSHParser#explicitEvaluationString}.
	 * @param ctx the parse tree
	 */
	void enterExplicitEvaluationString(PennMUSHParser.ExplicitEvaluationStringContext ctx);
	/**
	 * Exit a parse tree produced by {@link PennMUSHParser#explicitEvaluationString}.
	 * @param ctx the parse tree
	 */
	void exitExplicitEvaluationString(PennMUSHParser.ExplicitEvaluationStringContext ctx);
	/**
	 * Enter a parse tree produced by {@link PennMUSHParser#explicitFunction}.
	 * @param ctx the parse tree
	 */
	void enterExplicitFunction(PennMUSHParser.ExplicitFunctionContext ctx);
	/**
	 * Exit a parse tree produced by {@link PennMUSHParser#explicitFunction}.
	 * @param ctx the parse tree
	 */
	void exitExplicitFunction(PennMUSHParser.ExplicitFunctionContext ctx);
	/**
	 * Enter a parse tree produced by {@link PennMUSHParser#function}.
	 * @param ctx the parse tree
	 */
	void enterFunction(PennMUSHParser.FunctionContext ctx);
	/**
	 * Exit a parse tree produced by {@link PennMUSHParser#function}.
	 * @param ctx the parse tree
	 */
	void exitFunction(PennMUSHParser.FunctionContext ctx);
	/**
	 * Enter a parse tree produced by {@link PennMUSHParser#funArguments}.
	 * @param ctx the parse tree
	 */
	void enterFunArguments(PennMUSHParser.FunArgumentsContext ctx);
	/**
	 * Exit a parse tree produced by {@link PennMUSHParser#funArguments}.
	 * @param ctx the parse tree
	 */
	void exitFunArguments(PennMUSHParser.FunArgumentsContext ctx);
	/**
	 * Enter a parse tree produced by {@link PennMUSHParser#genericText}.
	 * @param ctx the parse tree
	 */
	void enterGenericText(PennMUSHParser.GenericTextContext ctx);
	/**
	 * Exit a parse tree produced by {@link PennMUSHParser#genericText}.
	 * @param ctx the parse tree
	 */
	void exitGenericText(PennMUSHParser.GenericTextContext ctx);
}