lexer grammar PennMUSHLexer;

/*
 * Lexer Rules   
 */

ESCAPE: '\\' -> pushMode(ESCAPING);
FUNCHAR
    : [a-zA-Z0-9]+? // Lazy way of indicating printable characters. There's more printable characters than this!
    ;
OBRACK: '[';
CBRACK: ']';
OBRACE: '{';
CBRACE: '}';
OPAREN: '(';
CPAREN: ')';
COMMA: ',';
EQUALS: '=';
DOLLAR: '$';
PERCENT: '%';
SEMICOLON: ';';
COLON: ':';
OANSI: '\u001B';
WS: ' '; 

// --------------- ESCAPING MODE -----------------
mode ESCAPING; 
UNESCAPE: '\\' -> skip, popMode;
OTHER: ~'\\' -> popMode;
