parser grammar PennMUSHParser;

options { 
    tokenVocab=PennMUSHLexer; 
}

/*
 * Parser Rules  
 */

evaluationString 
    : function explicitEvaluationString 
    | explicitFunction explicitEvaluationString
    | function
    | explicitFunction
    | genericText
    ;
explicitEvaluationString
    : explicitFunction explicitEvaluationString 
    | explicitFunction 
    | genericText
    ;
explicitFunction 
    : OBRACK function CBRACK
    ;
function 
    : FUNCHAR+ OPAREN funArguments CPAREN
    ;
funArguments 
    : evaluationString COMMA
    ;
genericText 
    : (.*?)
    ; 