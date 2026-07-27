


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
ownerExpr: OWNER string;
carryExpr: CARRY string;
bitFlagExpr: BIT_FLAG string;
bitPowerExpr: BIT_POWER string;
bitTypeExpr: BIT_TYPE objectType;

objectType: STRING;
channelExpr: CHANNEL string;
dbRefListExpr: DBREFLIST string;
ipExpr: IP string;
hostNameExpr: HOSTNAME string;
nameExpr: NAME string;
exactObjectExpr: EXACTOBJECT string (ATTRIBUTE_COLON string)?;
attributeExpr: string ATTRIBUTE_COLON string;
evaluationExpr: string EVALUATION string;
defaultExpr: string;

indirectExpr:
    INDIRECT string EVALUATION string
    | INDIRECT string
;

string: STRING;