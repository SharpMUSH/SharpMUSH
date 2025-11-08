


parser grammar SharpMUSHBoolExpParser;

options {
    tokenVocab = SharpMUSHBoolExpLexer;
}

/*
 * Parser Rules  
 */

lock: lockExprList EOF;

lockExprList: lockAndExpr | lockOrExpr | lockExpr;

lockAndExpr: lockExpr AND lockExprList;

lockOrExpr: lockExpr OR lockExprList;

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
;

notExpr: NOT lockExpr;
falseExpr: FALSE;
trueExpr: TRUE;
enclosedExpr: OPEN lockExprList CLOSE;
ownerExpr: OWNER string;
carryExpr: CARRY string;
bitFlagExpr: BIT_FLAG CARET_TOKEN string;
bitPowerExpr: BIT_POWER CARET_TOKEN string;
bitTypeExpr: BIT_TYPE CARET_TOKEN objectType;

objectType: STRING;
channelExpr: CHANNEL CARET_TOKEN string;
dbRefListExpr: DBREFLIST CARET_TOKEN attributeName;
ipExpr: IP CARET_TOKEN string;
hostNameExpr: HOSTNAME CARET_TOKEN string;
nameExpr: NAME CARET_TOKEN string;
exactObjectExpr: EXACTOBJECT string;
attributeExpr: attributeName ATTRIBUTE_COLON string;
evaluationExpr: attributeName EVALUATION string;

indirectExpr:
    INDIRECT string EVALUATION attributeName
    | INDIRECT string
;

string: STRING;

attributeName: ATTRIBUTENAME;