


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
fragment M: [nN];
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
NAME: N A M E;
BIT_FLAG: F L A G;
BIT_POWER: P O W E R;
BIT_TYPE: T Y P E;
DBREFLIST: D B R E F L I S T;
CHANNEL: C H A N N E L;
IP: I P;
HOSTNAME: H O S T N A M E;
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
CARET_TOKEN: '^';
ATTRIBUTE_COLON: ':';
ATTRIBUTENAME:
    ~('#' | '&' | '|' | ':' | '!' | ')' | '(' | '/' | ' ' | '^')+
;
// STRING - must come LAST so keywords match first
STRING: ~( '#' | '&' | '|' | ':' | '!' | ')' | '(' | '^' | ' ' | '/')+;