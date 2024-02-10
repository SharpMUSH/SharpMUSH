lexer grammar PennMUSHLexer;

/*
 * Lexer Rules   
 */

FUNCHAR 
    : (([a-zA-Z0-9])+?)
    ;
OBRACK: '[';
CBRACK: ']';
OPAREN: '(';
CPAREN: ')';
COMMA: ',';