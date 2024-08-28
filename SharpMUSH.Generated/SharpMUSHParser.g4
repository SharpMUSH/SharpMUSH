


parser grammar SharpMUSHParser;

options {
    tokenVocab = SharpMUSHLexer;
}

@parser::members {
    private int inFunction = 0;
    private bool inCommandMatch = false;
    private bool inCommandList = false;
    private bool lookingForCommandArgCommas = false;
    private bool lookingForCommandArgEquals = false;
    private bool lookingForRegisterCaret = false;
}

/*
 * Parser Rules  
 * TODO: Support {} behavior in functions and commands.
 */

// Start a single command, as run by a player.
startSingleCommandString: command EOF;

// Start a command list, as run by an object.
startCommandString:
    {inCommandList = true;} commandList EOF {inCommandList = false;}
;

// Start looking for a pattern with comma separated arguments.
startPlainCommaCommandArgs: commaCommandArgs EOF;

// Start looking for a pattern with an '=' split, followed by comma separated arguments.
startEqSplitCommandArgs:
    singleCommandArg (EQUALS commaCommandArgs)? EOF
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

commandList: command (SEMICOLON command)*?;

command:
    firstCommandMatch (
        RSPACE {inCommandMatch = false;} evaluationString
    )?
;

firstCommandMatch: {inCommandMatch = true;} evaluationString;

commaCommandArgs:
    {lookingForCommandArgCommas = true;} singleCommandArg (
        COMMAWS singleCommandArg
    )*? {lookingForCommandArgCommas = false;}
;


singleCommandArg: evaluationString;

evaluationString:
    function explicitEvaluationString?
    | explicitEvaluationString
;

explicitEvaluationString:
    OBRACE explicitEvaluationString CBRACE explicitEvaluationStringConcatenatedRepeat*?
    | OBRACK evaluationString CBRACK explicitEvaluationStringConcatenatedRepeat*?
    | PERCENT validSubstitution explicitEvaluationStringConcatenatedRepeat*?
    | beginGenericText explicitEvaluationStringConcatenatedRepeat*?
;

explicitEvaluationStringConcatenatedRepeat:
    OBRACE explicitEvaluationString CBRACE
    | OBRACK evaluationString CBRACK
    | PERCENT validSubstitution
    | genericText
;

funName:
    FUNCHAR {++inFunction;}
; // TODO: A Substitution can be inside of a funName to create a function name. The same goes for [] calls.

function: funName funArguments? {--inFunction;} CPAREN;

funArguments: evaluationString (COMMAWS evaluationString)*?;

validSubstitution:
    complexSubstitutionSymbol
    | substitutionSymbol
;

complexSubstitutionSymbol: (
        REG_STARTCARET {lookingForRegisterCaret = true;} explicitEvaluationString*? CCARET {lookingForRegisterCaret = false;
            }
        | REG_NUM
        | ITEXT_NUM
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
    | {inFunction == 0}? CPAREN
    | {!inCommandMatch || inFunction > 0}? RSPACE
    | {!inCommandList}? SEMICOLON
    | {!lookingForCommandArgCommas && inFunction == 0}? COMMAWS
    | {!lookingForCommandArgEquals}? EQUALS
    | {!lookingForRegisterCaret}? CCARET
    | (COLON | OTHER)
;

escapedText: ESCAPE ANY;

ansi: OANSI ANSICHARACTER? CANSI;