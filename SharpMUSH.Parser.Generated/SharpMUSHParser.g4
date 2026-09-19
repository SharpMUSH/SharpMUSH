parser grammar SharpMUSHParser;

options {
    tokenVocab = SharpMUSHLexer;
}

@parser::members {
    public int inFunction = 0;
    public int inBraceDepth = 0;
    public int inBracketDepth = 0;
    public int inFunctionInsideBrace = 0;
    public System.Collections.Generic.Stack<int> savedFunctionInsideBrace = new();
    // Literal '(' groups open at the current call or brace level; see beginGenericText.
    // Counted only when parenGroups is set (the paren_groups compatibility option).
    public bool parenGroups = false;
    public int inParenDepth = 0;
    public System.Collections.Generic.Stack<int> savedParenDepth = new();
    public System.Collections.Generic.Stack<int> savedFunction = new();
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
    {lookingForCommandArgEquals = true;} evaluationString? (
      EQUALS {lookingForCommandArgEquals = false;} commaCommandArgs
    )? EOF
;

// Start looking for a pattern, with a '=' split, but without comma separated arguments.
startEqSplitCommand:
    {lookingForCommandArgEquals = true;} evaluationString? (
        EQUALS {lookingForCommandArgEquals = false;} evaluationString?
    )? EOF
; 

// Start looking for a single-argument command value, by parsing the argument.
startPlainSingleCommandArg: evaluationString? EOF;

// Start looking for a plain string. These may start with a function call.
startPlainString: evaluationString EOF;

commandList: command ({inBraceDepth == 0}? SEMICOLON command?)*;

command: evaluationString;

commaCommandArgs:
    {lookingForCommandArgCommas = true;} evaluationString? (
        {inBraceDepth == 0}? COMMAWS evaluationString?
    )* {lookingForCommandArgCommas = false;}
;

evaluationString:
      function explicitEvaluationString?
    | explicitEvaluationString
;

explicitEvaluationString:
    (bracePattern|bracketPattern|beginGenericText|PERCENT validSubstitution|regexpCapture) 
    (
        bracePattern
      | bracketPattern
      | PERCENT validSubstitution
      | regexpCapture
      | genericText
    )*
;

// Like explicitEvaluationString but accepts FUNCHAR as a first element.
// Used inside bracePattern where function names should be treated as generic text
// (not recognized as function calls) per PennMUSH semantics.
// Cannot use evaluationString here as it introduces recursive prediction
// paths through the function rule that cause AdaptivePredict to hang on complex inputs.
braceExplicitEvaluationString:
    (bracePattern|bracketPattern|genericText|PERCENT validSubstitution|regexpCapture) 
    (
        bracePattern
      | bracketPattern
      | PERCENT validSubstitution
      | regexpCapture
      | genericText
    )*
;

// $0-$9 or $<name>. The name is evaluated up to the '>', whether or not a regexp context is live:
// PennMUSH only decides afterwards whether to look it up or to print it after a literal '$'.
regexpCapture
    locals [bool outerCaret]
    : REGEXP_NUM
    | REGEXP_STARTCARET { $outerCaret = lookingForRegisterCaret; lookingForRegisterCaret = true; }
      explicitEvaluationString? CCARET? { lookingForRegisterCaret = $outerCaret; }
;

bracePattern:
    OBRACE { ++inBraceDepth; savedFunctionInsideBrace.Push(inFunctionInsideBrace); inFunctionInsideBrace = 0; savedFunction.Push(inFunction); inFunction = 0; savedParenDepth.Push(inParenDepth); inParenDepth = 0; } braceExplicitEvaluationString? CBRACE { --inBraceDepth; inFunctionInsideBrace = savedFunctionInsideBrace.Pop(); inFunction = savedFunction.Pop(); inParenDepth = savedParenDepth.Pop(); }
;

bracketPattern:
    OBRACK { ++inBracketDepth; } evaluationString CBRACK { --inBracketDepth; }
;

function: 
    FUNCHAR {++inFunction; ++inFunctionInsideBrace; savedParenDepth.Push(inParenDepth); inParenDepth = 0;} 
    (evaluationString? (COMMAWS evaluationString?)*)?
    CPAREN {--inFunction; --inFunctionInsideBrace; inParenDepth = savedParenDepth.Pop();} 
;

validSubstitution:
    complexSubstitutionSymbol
    | substitutionSymbol
;

complexSubstitutionSymbol: (
        REG_STARTCARET {lookingForRegisterCaret = true;} explicitEvaluationString CCARET {lookingForRegisterCaret = false;}
        | REG_NUM
        | REG_ALPHA
        | ITEXT_NUM
        | ITEXT_LAST
        | STEXT_NUM
        | STEXT_LAST
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

// A name whose '(' is not in call position is text, and its '(' opens a literal group.
genericText: beginGenericText | FUNCHAR { if (parenGroups) ++inParenDepth; };

// A '(' that does not start a function call opens a literal group. PennMUSH copies the '(',
// evaluates up to the matching ')' with that as the only terminator, and copies the ')' (src/parse.c,
// case '('), so the group's commas are text and its ')' does not close an enclosing call. Groups are
// counted, not nested as rules, so a bare '(' costs no parser recursion however deep it goes; a call
// or brace saves the count and starts its own. Only with parenGroups: otherwise a '(' is plain text
// and a literal parenthesis inside a call's arguments has to be escaped.
beginGenericText:
      { inFunction == 0 || inParenDepth > 0 }? CPAREN { if (inParenDepth > 0) --inParenDepth; }
    | { !inCommandList || inBraceDepth > 0 }? SEMICOLON
    | { (!lookingForCommandArgCommas && inFunction == 0) || (inBraceDepth > 0 && inFunctionInsideBrace == 0) || inParenDepth > 0 }? COMMAWS
    | { !lookingForCommandArgEquals || inFunction > 0 }? EQUALS
    | { !lookingForRegisterCaret }? CCARET
    | OPAREN { if (parenGroups) ++inParenDepth; }
    | (escapedText|OTHER|DOLLAR|ansi) 
;

escapedText: ESCAPE ANY;

ansi: OANSI ANSICHARACTER? CANSI;