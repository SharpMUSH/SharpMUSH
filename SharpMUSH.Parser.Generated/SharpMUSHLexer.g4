


lexer grammar SharpMUSHLexer;

/*
 * Lexer Rules   
 */

fragment WS: [ \r\n\f\t]*;
// RhostMUSH's bang notation on a call, %i<n> or %d<n>: ! or !! and an optional $ or ^ (Bang.cs).
fragment BANG: '!' '!'? [$^]?;

// A '\' or '%' that ends the text has nothing to escape or substitute: PennMUSH drops the '\' and keeps
// the '%' as text (src/parse.c). Each is one token, so neither is left waiting for a character.
ESCAPE_AT_END: '\\' EOF;
PERCENT_AT_END: '%' EOF;
ESCAPE: '\\' -> pushMode(ESCAPING);
OBRACK: '[' WS;
CBRACK: ']';
OBRACE: '{' WS;
CBRACE: '}';
CPAREN: ')';
CCARET: '>';
COMMAWS: ',' WS;
EQUALS: '=' WS;
PERCENT: '%' -> pushMode(SUBSTITUTION);
SEMICOLON: ';' WS;
OANSI: '\u001b' -> pushMode(ANSI);
FUNCHAR: BANG? [0-9a-zA-Z_~@`]+ '(' WS ;
OPAREN: '(' WS;

// Regexp captures: $0-$9 (one digit, as in PennMUSH) and $<name>. They read the regexp context that
// reswitch() opens, and are a plain '$' outside one (src/parse.c, case '$').
REGEXP_NUM: '$' [0-9];
REGEXP_STARTCARET: '$<';
// Any other '$' is text. OTHER stops at every '$' so that it cannot swallow a following $0.
DOLLAR: '$';

// ## and #@: an iter() or @dolist element and its position, and text anywhere else. A '#' that does
// not start one stays inside OTHER, so a dbref in text does not break the run; HASH is a '#' that
// OTHER cannot take (one at the end, or before a special character).
ITER_ELEMENT: '##';
ITER_POSITION: '#@';
// #$: the subject of the switch()/@switch it is written in, and text anywhere else. OTHER already stops
// at '$', so this costs nothing where it is absent.
SWITCH_SUBJECT: '#$';
HASH: '#';

// Greedy way of grabbing non-special characters which the parser does not care about, and can thus fast-forward through.
OTHER: ( ~( '\\' | '[' | ']' | '{' | '}' | '(' | ')' | '>' | ',' | '=' | '%' | ';' | '\u001b' | '$' | '#' ) | '#' ~( '\\' | '[' | ']' | '{' | '}' | '(' | ')' | '>' | ',' | '=' | '%' | ';' | '\u001b' | '$' | '#' | '@' ) )+;

// --------------- SUBSTITUTION MODE -------------
mode SUBSTITUTION;
COLON: ':' -> popMode;
REG_STARTCARET: [qQ]'<' -> popMode;
REG_NUM: [qQ][0-9] -> popMode;
REG_ALPHA: [qQ][a-zA-Z] -> popMode;
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
PIPED_OUTPUT: '>' -> popMode;
INVOCATION_DEPTH: '?' -> popMode;
CURRENT_ARG_COUNT: '+' -> popMode;
ITEXT_NUM: [iI] BANG? [0-9]+ -> popMode;
// %iL, and RhostMUSH's %il and %i!L. PennMUSH has only %iL; %il was the text "il".
ITEXT_LAST: [iI] BANG? [lL] -> popMode;
// RhostMUSH's %i_<n> and %i_L: the position of an iteration's element, like inum(). Text in PennMUSH.
ITEXT_POS_NUM: [iI] '_' BANG? [0-9]+ -> popMode;
ITEXT_POS_LAST: [iI] '_' BANG? [lL] -> popMode;
// RhostMUSH's %-<n>: argument n, 0-999, up to three digits. PennMUSH prints "-<n>".
ARG_DASH_NUM: '-' BANG? [0-9] [0-9]? [0-9]? -> popMode;
DTEXT_NUM: [dD] BANG? [0-9]+ -> popMode;
DTEXT_LAST: [dD][lL] -> popMode;
STEXT_NUM: '$' [0-9]+ -> popMode;
STEXT_LAST: '$' 'L' -> popMode;
ARG_STARTCARET: '<' -> popMode;
// %=<name>, from TinyMUX: the executor's attribute <name>, or argument <n> when it is a number.
ATTR_STARTCARET: '=<' -> popMode;
OTHER_SUB: . -> popMode;

// --------------- ESCAPING MODE -----------------
mode ESCAPING;
ANY: . -> popMode;

// --------------- ANSI MODE ---------------------
mode ANSI;
CANSI: 'm' -> popMode;
ANSICHARACTER: ~'m'+;