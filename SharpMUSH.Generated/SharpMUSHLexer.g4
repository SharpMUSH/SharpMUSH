


lexer grammar SharpMUSHLexer;

/*
 * Lexer Rules   
 */

fragment WS: [ \r\n\f\t]*;

ESCAPE: '\\' -> pushMode(ESCAPING);
OBRACK: '[' WS;
CBRACK: WS ']';
OBRACE: '{' WS;
CBRACE: WS '}';
CPAREN: WS ')';
CCARET: '>';
COMMAWS: WS ',' WS;
EQUALS: WS '=' WS;
PERCENT: '%' -> pushMode(SUBSTITUTION);
SEMICOLON: WS ';' WS;
OANSI: '\u001b' -> pushMode(ANSI);
FUNCHAR: [a-zA-Z0-9_]+ '(' WS ; 

// Greedy way of grabbing non-special characters which the parser does not care about, and can thus fast-forward through.
OTHER: ~('\\' | '[' | ']' | '{' | '}' | '(' | ')' | '<' | '>' | ',' | '%' | '$' | ';' | ':' | '\u001b' | ' ' | '=')+;
ANY_AT_ALL: .+?;

// --------------- SUBSTITUTION MODE -------------
// TODO: Remove all the single-character cases, and let the code itself figure out what they are.
mode SUBSTITUTION;
COLON: ':' -> popMode;
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
ITEXT_LAST: [iI] 'L' -> popMode;
STEXT_NUM: '$' [0-9]+ -> popMode;
OTHER_SUB: . -> popMode;

// --------------- ESCAPING MODE -----------------
mode ESCAPING;
ANY: . -> popMode;

// --------------- ANSI MODE ---------------------
mode ANSI;
CANSI: 'm' -> popMode;
ANSICHARACTER: ~'m'+;