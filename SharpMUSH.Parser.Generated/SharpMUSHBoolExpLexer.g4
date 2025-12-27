


lexer grammar SharpMUSHBoolExpLexer;

/*
 * Lexer Rules   
 */

fragment CARET: '^';
fragment POUND: '#';
fragment F: [fF];
fragment A: [aA];
fragment L: [lL];
fragment S: [sS];
fragment E: [eE];
fragment T: [tT];
fragment R: [rR];
fragment U: [uU];
fragment N: [nN];
fragment M: [mM];
fragment G: [gG];
fragment P: [pP];
fragment O: [oO];
fragment W: [wW];
fragment Y: [yY];
fragment D: [dD];
fragment B: [bB];
fragment C: [cC];
fragment H: [hH];
fragment I: [iI];
fragment J: [jJ];
fragment WS: ' ';

// Keyword lock tokens - MUST come first to have priority over STRING
// Include CARET in token definition so lexer can distinguish "name" from "name^"
NAME: N A M E CARET;
BIT_FLAG: F L A G CARET;
BIT_POWER: P O W E R CARET;
BIT_TYPE: T Y P E CARET;
DBREFLIST: D B R E F L I S T CARET;
CHANNEL: C H A N N E L CARET;
IP: I P CARET;
HOSTNAME: H O S T N A M E CARET;
// Special symbols and operators
OPEN: WS* '(' WS*;
CLOSE: WS* ')' WS*;
NOT: '!';
AND: WS* '&' WS*;
OR: WS* '|' WS*;
CARRY: '+';
OWNER: '$';
INDIRECT: '@';
EVALUATION: '/';
EXACTOBJECT: '=';
FALSE: POUND F A L S E;
TRUE: POUND T R U E;
ATTRIBUTE_COLON: ':';
// STRING - must come BEFORE ATTRIBUTENAME so it matches in `string` parser rules
// Exclude operator characters but allow # (needed for dbrefs like #1, #123)
// FALSE/TRUE tokens will match #FALSE and #TRUE before STRING can
STRING: ~( '&' | '|' | ':' | '!' | ')' | '(' | '^' | ' ' | '/' | '+' | '$' | '@' | '=')+;
ATTRIBUTENAME:
    ~('&' | '|' | ':' | '!' | ')' | '(' | '/' | ' ' | '^' | '+' | '$' | '@' | '=')+
;