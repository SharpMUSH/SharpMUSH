parser grammar PennMUSHParser;

options { 
    tokenVocab=PennMUSHLexer; 
}

/*
 * Parser Rules  
 */

plainString: evaluationString EOF;

evaluationString 
    : function explicitEvaluationString*?
    | explicitEvaluationString
    ;
explicitEvaluationString
    : explicitFunction explicitEvaluationString*?
    | genericText explicitEvaluationString*?
    ;
explicitFunction
    : OBRACK function CBRACK
    ;
function 
    : funName OPAREN CPAREN
    | funName OPAREN funArguments CPAREN
    ;
funName 
    : FUNCHAR+
    ;
funArguments
    : evaluationString (COMMA evaluationString)*?
    ;
genericText 
    : (~ESCAPE)+? // This is ignoring space? 
    | ESCAPE
    ;