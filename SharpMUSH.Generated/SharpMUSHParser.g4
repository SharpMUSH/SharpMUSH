


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
singleCommandString: command EOF;

commandString:
    {inCommandList = true;} commandList EOF {inCommandList = false;}
;

commandList: command (SEMICOLON command)*?;

command:
    firstCommandMatch RSPACE {inCommandMatch = false;} evaluationString
    | firstCommandMatch
;

firstCommandMatch: {inCommandMatch = true;} evaluationString;

eqsplitCommandArgs:
    singleCommandArg (EQUALS commaCommandArgs)? EOF
;

eqsplitCommand:
    {lookingForCommandArgEquals = true;} singleCommandArg (
        EQUALS singleCommandArg
    )? EOF {lookingForCommandArgEquals = false;}
;

commaCommandArgs:
    {lookingForCommandArgCommas = true;} singleCommandArg (
        COMMAWS singleCommandArg
    )*? EOF {lookingForCommandArgCommas = false;}
;

plainSingleCommandArg: singleCommandArg EOF;

singleCommandArg: evaluationString;

plainString: evaluationString EOF;

evaluationString:
    function explicitEvaluationString?
    | explicitEvaluationString
;

explicitEvaluationString: explicitEvaluationStringContents;

explicitEvaluationStringContents:
    OBRACE explicitEvaluationString CBRACE explicitEvaluationStringContentsConcatenated?
    | OBRACK evaluationString CBRACK explicitEvaluationStringContentsConcatenated?
    | PERCENT validSubstitution explicitEvaluationStringContentsConcatenated?
    | startGenericText explicitEvaluationStringContentsConcatenated?
;

explicitEvaluationStringContentsConcatenated:
    OBRACE explicitEvaluationString CBRACE explicitEvaluationStringContentsConcatenated?
    | OBRACK evaluationString CBRACK explicitEvaluationStringContentsConcatenated?
    | PERCENT validSubstitution explicitEvaluationStringContentsConcatenated?
    | genericText+? explicitEvaluationStringContentsConcatenated?
;

funName:
    FUNCHAR {++inFunction;}
; // TODO: A Substitution can be inside of a funName to create a function name.

function: funName (funArguments)? CPAREN {--inFunction;};

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

genericText:
    escapedText
    | ansi
    | awareGenericText
    | (FUNCHAR | COLON | OTHER)
;

startGenericText:
    escapedText
    | ansi
    | awareGenericText
    | (COLON | OTHER)
;

awareGenericText:
    {inFunction == 0}? CPAREN
    | {!inCommandMatch}? RSPACE
    | {!inCommandList}? SEMICOLON
    | {!lookingForCommandArgCommas && inFunction == 0}? COMMAWS
    | {!lookingForCommandArgEquals}? EQUALS
    | {!lookingForRegisterCaret}? CCARET
;

escapedText: ESCAPE ANY;
ansi: OANSI ANSICHARACTER? CANSI;