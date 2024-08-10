lexer grammar SharpMUSHLexer;

/*
 * Lexer Rules   
 */

OPARENWS: '(' [ \r\n\f\t]*;
ESCAPE: '\\' -> pushMode(ESCAPING);
OBRACK: '[';
CBRACK: ']';
OBRACE: '{' -> skip;
CBRACE: '}' -> skip;
CPAREN: ')';
CCARET: '>';
COMMAWS: ',' [ \r\n\f\t]*;
EQUALS: '=';
PERCENT: '%' -> pushMode(SUBSTITUTION);
SEMICOLON: ';';
COLON: ':';
OANSI: '\u001B' -> pushMode(ANSI);
RSPACE: ' ';
FUNCHAR
    : [a-zA-Z0-9]+ // Lazy way of indicating printable characters. There's more printable characters than this!
    ;
OTHER: ~('\\'|'['|']'|'{'|'}'|'('|')'|'<'|'>'|','|'='|'$'|'%'|';'|':'|'\u001B'|' ')+;

// --------------- SUBSTITUTION MODE -------------
mode SUBSTITUTION;
REG_STARTCARET: [qQ]'<' -> popMode;
REG_NUM: [qQ][0-9] -> popMode;
VWX: [vwxVWX][a-zA-Z] -> popMode;
ARG_NUM: [0-9] -> popMode; 
SPACE: [bB] -> popMode;
BLANKLINE: [rR] -> popMode;
TAB: [tT] -> popMode;
DBREF: '#' -> popMode;
ENACTOR_NAME: 'n' -> popMode;
CAP_ENACTOR_NAME: 'N' -> popMode;
ACCENT_NAME: '~' -> popMode;
MONIKER_NAME: [kK] -> popMode;
SUB_PRONOUN: [sS] -> popMode;
OBJ_PRONOUN: [oO] -> popMode;
POS_PRONOUN: [pP] -> popMode;
ABS_POS_PRONOUN: [aA] -> popMode;
CALLED_DBREF: '@' -> popMode;
EXECUTOR_DBREF: '!' -> popMode;
LOCATION_DBREF: [lL] -> popMode;
LASTCOMMAND_BEFORE_EVAL: [cC] -> popMode;
LASTCOMMAND_AFTER_EVAL: [uU] -> popMode;
INVOCATION_DEPTH: '?' -> popMode;
CURRENT_ARG_COUNT: '+' -> popMode;
ITEXT_NUM: [iI][0-9]+ -> popMode;
STEXT_NUM: '$'[0-9]+ -> popMode;
OTHER_SUB: . -> popMode;

// --------------- ESCAPING MODE -----------------
mode ESCAPING; 
UNESCAPE: '\\' -> skip, popMode;
ESCAPING_OTHER: ~'\\' -> popMode;

// --------------- ANSI MODE ---------------------
mode ANSI;
CANSI: 'm' -> popMode;
ANSICHARACTER: ~'m'+;