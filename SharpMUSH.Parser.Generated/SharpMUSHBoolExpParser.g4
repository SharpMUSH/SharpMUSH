


parser grammar SharpMUSHBoolExpParser;

options {
    tokenVocab = SharpMUSHBoolExpLexer;
}

/*
 * Parser Rules  
 */

lock: lockExprList EOF;

// Operator precedence, mirroring PennMUSH's boolexp grammar (src/boolexp.c):
//     E -> T | E      (OR, lowest precedence)
//     T -> F & T      (AND, binds tighter than OR)
//     F -> !F | A     (NOT, binds tightest — see notExpr below)
// so `a & b | c` groups as `(a & b) | c`. A flat `lockAndExpr | lockOrExpr | lockExpr`
// alternation would instead give both operators equal precedence and parse this as
// `a & (b | c)`, silently changing who passes a lock.
lockExprList: lockOrExpr;

lockOrExpr: lockAndExpr (OR lockAndExpr)*;

lockAndExpr: lockExpr (AND lockExpr)*;

lockExpr:
    notExpr
    | enclosedExpr
    | falseExpr
    | trueExpr
    | ownerExpr
    | carryExpr
    | indirectExpr
    | bitFlagExpr
    | bitPowerExpr
    | bitTypeExpr
    | channelExpr
    | dbRefListExpr
    | ipExpr
    | hostNameExpr
    | nameExpr
    | exactObjectExpr
    | attributeExpr
    | evaluationExpr
    | defaultExpr
;

notExpr: NOT lockExpr;
falseExpr: FALSE;
trueExpr: TRUE;
enclosedExpr: OPEN lockExprList CLOSE;
ownerExpr: OWNER objectOperand;
carryExpr: CARRY objectOperand;
bitFlagExpr: BIT_FLAG literal;
bitPowerExpr: BIT_POWER literal;
bitTypeExpr: BIT_TYPE objectType;

objectType: STRING;
channelExpr: CHANNEL literal;
dbRefListExpr: DBREFLIST literal;
ipExpr: IP literal;
hostNameExpr: HOSTNAME literal;
nameExpr: NAME literal;
exactObjectExpr: EXACTOBJECT objectOperand;
attributeExpr: string ATTRIBUTE_COLON literal;
evaluationExpr: string EVALUATION literal;
defaultExpr: string;

indirectExpr:
    INDIRECT objectOperand EVALUATION string
    | INDIRECT objectOperand
;

string: STRING | STAMPED_DBREF;
objectOperand: string (ATTRIBUTE_COLON string)*;

// Boolean separators terminate a value; other punctuation belongs to the literal.
// Escaped separators are part of STRING and retain their wildcard meaning.
literal: (STRING | STAMPED_DBREF | ATTRIBUTE_COLON | EVALUATION | NOT | CARRY
    | OWNER | INDIRECT | EXACTOBJECT | OPEN | TRUE | FALSE | NAME | BIT_FLAG
    | BIT_POWER | BIT_TYPE | DBREFLIST | CHANNEL | IP | HOSTNAME | LITERAL_CARET)+;
