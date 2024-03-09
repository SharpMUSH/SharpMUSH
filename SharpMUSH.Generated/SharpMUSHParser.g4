parser grammar SharpMUSHParser;

options { 
    tokenVocab=SharpMUSHLexer; 
}

/*
 * Parser Rules  
 * TODO: Support {} behavior in functions and commands.
 */

singleCommandString
    : command EOF
    ;

commandString
    : commandList EOF
    ;

commandList
    : command (SEMICOLON command)*
    ;

command
    : firstCommandMatch RSPACE evaluationString
    | firstCommandMatch
    ;

firstCommandMatch
    : evaluationString
    ;

plainString
    : evaluationString EOF
    ;

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
funName  // TODO: A Substitution can be inside of a funName to create a function name.
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
    : complexSubstitutionSymbol
    | substitutionSymbol
    ;
complexSubstitutionSymbol
    : REG_STARTCARET explicitEvaluationString+? CCARET
    | REG_NUM
    | ITEXT_NUM
    | STEXT_NUM
    | VWX
    ;
substitutionSymbol
    : SPACE
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
    | ARG_NUM
    | CALLED_DBREF
    | EXECUTOR_DBREF
    | LOCATION_DBREF
    | LASTCOMMAND_BEFORE_EVAL
    | LASTCOMMAND_AFTER_EVAL
    | INVOCATION_DEPTH    
    | EQUALS
    | CURRENT_ARG_COUNT
    ;
genericText 
    : escapedText
    | ansi
    | OTHER
    | .
    ;
escapedText
    : ESCAPE UNESCAPE
    | ESCAPE ESCAPING_OTHER
    ;
ansi
    : OANSI ANSICHARACTER? CANSI
    ;