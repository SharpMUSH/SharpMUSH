


parser grammar SharpMUSHParser;

options {
    tokenVocab = SharpMUSHLexer;
}

@parser::members {
    public int inFunction = 0;
    public int inBraceDepth = 0;
    public bool inCommandList = false;
    public bool lookingForCommandArgCommas = false;
    public bool lookingForCommandArgEquals = false;
    public bool lookingForRegisterCaret = false;
}

/*
 * Parser Rules  
 */

// Start a single command, as run by a player.
startSingleCommandString: command EOF;

// Start a command list, as run by an object.
startCommandString:
    {inCommandList = true;} commandList EOF {inCommandList = false; } 
;

// Start looking for a pattern with comma separated arguments.
startPlainCommaCommandArgs: commaCommandArgs EOF;

// Start looking for a pattern with an '=' split, followed by comma separated arguments.
startEqSplitCommandArgs:
    {lookingForCommandArgEquals = true;} singleCommandArg (
      EQUALS {lookingForCommandArgEquals = false;} commaCommandArgs
    )? EOF
;

// Start looking for a pattern, with a '=' split, but without comma separated arguments.
startEqSplitCommand:
    {lookingForCommandArgEquals = true;} singleCommandArg (
        EQUALS {lookingForCommandArgEquals = false;} singleCommandArg
    )? EOF
; 

// Start looking fora single-argument command value, by parsing the argument.
startPlainSingleCommandArg: singleCommandArg EOF;

// Start looking for a plain string. These may start with a function call.
startPlainString: evaluationString EOF;

commandList: command ({inBraceDepth == 0}? SEMICOLON command)*;

command: evaluationString;

commaCommandArgs:
    {lookingForCommandArgCommas = true;} singleCommandArg (
        {inBraceDepth == 0}? COMMAWS singleCommandArg
    )* {lookingForCommandArgCommas = false;}
;


singleCommandArg: evaluationString;

evaluationString:
    function explicitEvaluationString?
    | explicitEvaluationString
;

explicitEvaluationString:
    bracePattern explicitEvaluationStringConcatenatedRepeat*
    | bracketPattern explicitEvaluationStringConcatenatedRepeat*
    | PERCENT validSubstitution explicitEvaluationStringConcatenatedRepeat*
    | beginGenericText explicitEvaluationStringConcatenatedRepeat*
;

explicitEvaluationStringConcatenatedRepeat:
    bracePattern
    | bracketPattern
    | PERCENT validSubstitution
    | genericText
;

bracePattern:
    OBRACE { ++inBraceDepth; } explicitEvaluationString? CBRACE { --inBraceDepth; }
;

bracketPattern:
    OBRACK evaluationString CBRACK
;

funName:
    FUNCHAR {++inFunction;}
; // TODO: A Substitution can be inside of a funName to create a function name. The same goes for [] calls.

function: funName funArguments? CPAREN {--inFunction;} ;

funArguments: funArgument ({inBraceDepth == 0}? COMMAWS funArgument)*?;

funArgument: evaluationString;

validSubstitution:
    complexSubstitutionSymbol
    | substitutionSymbol
;

complexSubstitutionSymbol: (
        REG_STARTCARET {lookingForRegisterCaret = true;} explicitEvaluationString*? CCARET {lookingForRegisterCaret = false;
            }
        | REG_NUM
        | ITEXT_NUM
        | ITEXT_LAST
        | STEXT_NUM
        | VWX
    )
;

substitutionSymbol: (
        SPACE
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
        | OTHER_SUB
    )
;

genericText: beginGenericText | FUNCHAR;

beginGenericText:
    escapedText
    | ansi
    | { inFunction == 0 }? CPAREN
    | { !inCommandList || inBraceDepth > 0 }? SEMICOLON
    | { (!lookingForCommandArgCommas && inFunction == 0) || inBraceDepth > 0 }? COMMAWS
    | { !lookingForCommandArgEquals }? EQUALS
    | { !lookingForRegisterCaret }? CCARET
    | (OTHER|ANY_AT_ALL) 
;

escapedText: ESCAPE ANY;

ansi: OANSI ANSICHARACTER? CANSI;