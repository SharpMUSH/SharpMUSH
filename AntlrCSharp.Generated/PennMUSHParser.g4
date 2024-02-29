parser grammar PennMUSHParser;

options { 
    tokenVocab=PennMUSHLexer; 
}

/*
 * Parser Rules  
 * TODO: Support {} behavior in functions and commands.
 */

singleCommandString: command EOF;
commandString: commandList EOF;
commandList: command (SEMICOLON command)*;
command: evaluationString+?;
/*
    TODO: If a command is an @command, we should use evaluationString after the standard @command, switches and all.
    What's more, there's things to consider when it comes to their standard arguments.
*/

plainString: evaluationString EOF;

evaluationString 
    : OBRACE evaluationString CBRACE
    | function explicitEvaluationString*?
    | explicitEvaluationString
    ;
explicitEvaluationString
    : OBRACE explicitEvaluationString CBRACE
    | explicitEvaluationStringSubstitution explicitEvaluationString*?
    | explicitEvaluationStringFunction explicitEvaluationString*?
    | explicitEvaluationText explicitEvaluationString*?
    ;
explicitEvaluationStringSubstitution
    : PERCENT validSubstitution
    ;
explicitEvaluationStringFunction
    : OBRACK function CBRACK
    ;
explicitEvaluationText
    : genericText
    ;
funName 
    : FUNCHAR
    ;
function 
    : funName OPAREN CPAREN
    | funName OPAREN funArguments CPAREN
    ;
funArguments
    : evaluationString (COMMA evaluationString)+
    | evaluationString
    ;
validSubstitution
    : REG_STARTCARET explicitEvaluationString+? CCARET
    | SPACE
    | BLANKLINE
    | TAB
    | COLON
    | DBREF
    | ENACTOR_NAME
    | CAP_ENACTOR_NAME
    | ACCENT_NAME
    | MONIKER_NAME
    | PERCENT
    | SUB_PRONOUN
    | OBJ_PRONOUN
    | POS_PRONOUN
    | ABS_POS_PRONOUN
    | VWX
    | ARG_NUM
    | CALLED_DBREF
    | EXECUTOR_DBREF
    | LOCATION_DBREF
    | LASTCOMMAND_BEFORE_EVAL
    | LASTCOMMAND_AFTER_EVAL
    | INVOCATION_DEPTH    
    | EQUALS
    | CURRENT_ARG_COUNT
    | REG_NUM
    | ITEXT_NUM
    | STEXT_NUM
    ;
genericText 
    : escapedText
    | OTHER
    | .
    ;
escapedText
    : ESCAPE UNESCAPE
    | ESCAPE
    ;