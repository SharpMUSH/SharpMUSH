


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
bitFlagExpr: BIT_FLAG string;
bitPowerExpr: BIT_POWER string;
bitTypeExpr: BIT_TYPE string;
channelExpr: CHANNEL string;
dbRefListExpr: DBREFLIST attributeName;
ipExpr: IP string;
hostNameExpr: HOSTNAME string;
nameExpr: NAME string;
exactObjectExpr: EXACTOBJECT string;
attributeExpr: attributeName ATTRIBUTE_COLON string;
evaluationExpr: attributeName EVALUATION string;

indirectExpr:
    INDIRECT string EVALUATION attributeName
    | INDIRECT string
;

string: STRING;

attributeName: ATTRIBUTENAME;