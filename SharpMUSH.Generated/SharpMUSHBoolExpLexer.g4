lexer grammar SharpMUSHBoolExpLexer;

/*
 * Lexer Rules   
 */

fragment CARET: '^';

OPEN: '(';
CLOSE: ')';
NOT: '!';
AND: '&';
OR: '|';
CARRY: '+';
OWNER: '$';
INDIRECT: '@';
EVALUATION: '/';
EXACTOBJECT: ('=' | [oO] [bB] [jJ] [iI] [dD] CARET);
FALSE: '#' [fF] [aA] [lL] [sS] [eE];
TRUE: '#' [tT] [rR] [uU] [eE];
NAME: [nN] [aA] [mM] [eE] CARET;
BIT_FLAG: [fF] [lL] [aA] [gG] CARET;
BIT_POWER: [pP] [oO] [wW] [eE] [rR] CARET;
BIT_TYPE: [tT] [yY] [pP] [eE] CARET;
DBREFLIST: [dD] [bB] [rR] [eE] [fF] [lL] [iI] [sS] [tT] CARET;
CHANNEL: [cC] [hH] [aA] [nN] [nN] [eE] [lL] CARET;
IP: [iI] [pP] CARET;
HOSTNAME: [hH] [oO] [sS] [tT] [nN] [aA] [mM] [eE] CARET;
ATTRIBUTE_COLON: ':';
STRING: ~( '&' | '|' | ':' | '!' | ')' | '(' | '/' )+;
ATTRIBUTENAME: ~( '&' | '|' | ':' | '!' | ')' | '(' | '/' | ' ' )+;