# Oracle cases for the parser-parity work (see CoPilot Files/PARSER_IMPROVEMENT_HANDOFF.md).
# Every expectation below was produced by a real PennMUSH server, not read off the source.
#
# To run against PennMUSH:
#   git clone https://github.com/pennmush/pennmush && cd pennmush
#   env -i PATH=/usr/bin:/bin HOME=$HOME ./configure CPPFLAGS=-I/usr/include LDFLAGS=-L/usr/lib
#   env -i PATH=/usr/bin:/bin HOME=$HOME make -j8
#   cp <this file> test/ && cd test && perl runtest.pl test_parser_parity.t
# The env -i matters: a conda/homebrew ICU on the include path links against the system
# ICU of a different soversion and the final link fails on u_isprint_NN.
#
# Perl quoting: these are single-quoted strings, so write %# not \%# — a backslash reaches
# PennMUSH as an escape and the substitution comes back literal.

run tests:

# --- Unknown function names -------------------------------------------------
# Only [...] sets PE_FUNCTION_MANDATORY (src/parse.c), so a bare name is prose.
test('parity.unknown_bare', $god, 'think notafunction(bar)', '^notafunction\(bar\)$');
test('parity.unknown_bracket', $god, 'think [notafunction(bar)]', 'NOT FOUND');
test('parity.unknown_prose', $god, 'think Hello there(friend)', '^Hello there\(friend\)$');
# Arguments of a real call are evaluated with MANDATORY stripped, so an unknown name
# nested in them stays literal even though a bracket encloses the whole expression.
test('parity.unknown_in_args', $god, 'think [strcat(notafunction(1))]', '^notafunction\(1\)$');

# --- Contents of a demoted call still evaluate ------------------------------
# PE_FUNCTION_CHECK is cleared, PE_EVALUATE is not.
test('parity.demoted_call', $god, 'think notafunction(add(1,2))', '^notafunction\(add\(1,2\)\)$');
test('parity.demoted_bracket', $god, 'think notafunction([add(1,2)])', '^notafunction\(3\)$');
test('parity.demoted_sub', $god, 'think notafunction(%#)', '^notafunction\(#1\)$');
test('parity.demoted_sub_nested', $god, 'think notafunction(strlen(%#))', '^notafunction\(strlen\(#1\)\)$');
test('parity.demoted_sub_nested2', $god, 'think notafunction(add(%#,2))', '^notafunction\(add\(#1,2\)\)$');
# Function-argument braces clear function recognition the same way.
test('parity.brace_sub', $god, 'think strcat({%#})', '^#1$');
test('parity.brace_nested', $god, 'think strcat({strlen(%#)})', '^strlen\(#1\)$');
test('parity.brace_bracket', $god, 'think strcat({[strlen(%#)]})', '^2$');

# --- Argument parity --------------------------------------------------------
# Enforced in the function bodies, not the dispatch table: fun_letq rejects
# nargs % 2 != 1 and fun_setq rejects nargs % 2 != 0 (src/funmisc.c).
test('parity.letq_odd', $god, 'think letq(A,1,%qA)', '^1$');
test('parity.letq_5args', $god, 'think letq(A,1,B,2,%qA%qB)', '^12$');
test('parity.letq_even_rejected', $god, 'think letq(A,1)', 'ODD NUMBER');
test('parity.letq_even_rejected2', $god, 'think letq(A,1,B,2)', 'ODD NUMBER');
test('parity.setr_odd_rejected', $god, 'think setr(A,1,B)', 'EVEN NUMBER');
# CASE/CASEALL/SWITCH are fun_switch, which checks only minargs — no parity rule,
# and the canonical form with a trailing default is even-numbered.
test('parity.case_even_default', $god, 'think case(b,a,first,b,second,fallback)', '^second$');
test('parity.case_even_fallback', $god, 'think case(z,a,first,b,second,fallback)', '^fallback$');
test('parity.case_odd', $god, 'think case(a,a,first,b,second)', '^first$');
test('parity.caseall_even', $god, 'think caseall(b,a,first,b,second,fallback)', '^second$');

# --- Lock operator precedence ----------------------------------------------
# boolexp.c cascades E -> T | E, T -> F & T, so & binds tighter than |.
# `#FALSE & #FALSE | #TRUE` is (F&F)|T = true; read as F&(F|T) it would be false.
test('parity.lock_prec_setup', $god, '@lock me=#FALSE & #FALSE | #TRUE', 'locked');
test('parity.lock_prec_true', $god, 'think elock(me/Basic, me)', '^1$');
test('parity.lock_prec_setup2', $god, '@lock me=#TRUE & #FALSE | #FALSE', 'locked');
test('parity.lock_prec_false', $god, 'think elock(me/Basic, me)', '^0$');
test('parity.lock_prec_setup3', $god, '@lock me=#FALSE & (#FALSE | #TRUE)', 'locked');
test('parity.lock_prec_paren', $god, 'think elock(me/Basic, me)', '^0$');
