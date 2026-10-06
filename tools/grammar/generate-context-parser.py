#!/usr/bin/env python3
"""Writes SharpMUSHContextParser.g4: SharpMUSHParser.g4 with its semantic predicates moved into rule names.

SharpMUSHParser.g4 decides whether ',', ')', ';', '=' and '>' are text or structure with predicates
over counters the parser keeps (inFunction, inBraceDepth, inParenDepth, ...). This writes the same
language with no predicates: each rule exists once per context those counters can describe, and
a rule's name says which context it is in. A comma is a separator in function__Call_* rules and
text in Top_* ones because the rules say so, not because a predicate is checked.

Usage: generate-context-parser.py [--name GrammarName] > SharpMUSHContextParser.g4
"""
import argparse
import re
import sys
from collections import namedtuple

# What the predicates read, as a fixed set of facts.
#   mode  the entry point: plain | cmd (command list, ';' separates) | args (',' separates)
#         | eq (before the '=' of an '=' split)
#   F     inside a function call, since the last brace (a brace resets it, as inFunction does)
#   B     inside a brace, at any depth
#   K     inside a register or capture name (%q<...>, $<...>), where '>' ends the name
#   P     paren groups (paren_groups only): 0 none, 1 inside a group, whose ')' closes it,
#         2 in brackets or a name inside a group, where ')' is text and ',' is still text
#   G     paren_groups is on
Ctx = namedtuple('Ctx', 'mode F B K P G')

LETTER = dict(CPAREN='R', SEMICOLON='S', COMMAWS='C', EQUALS='E', CCARET='A')
SHOWN = dict(CPAREN="')'", SEMICOLON="';'", COMMAWS="','", EQUALS="'='", CCARET="'>'")


def text_tokens(c):
	"""The tokens beginGenericText accepts in context c; the predicates of SharpMUSHParser.g4."""
	tokens = []
	if c.P == 2 or (not c.F and not c.P):  # inFunction == 0 || inParenDepth > 0
		tokens.append('CPAREN')
	if c.mode != 'cmd' or c.B:  # !inCommandList || inBraceDepth > 0
		tokens.append('SEMICOLON')
	if c.P or (not c.F and (c.mode != 'args' or c.B)):  # see beginGenericText
		tokens.append('COMMAWS')
	if c.mode != 'eq' or c.F:  # !lookingForCommandArgEquals || inFunction > 0
		tokens.append('EQUALS')
	if not c.K:  # !lookingForRegisterCaret
		tokens.append('CCARET')
	return tuple(tokens)


# How each construct changes the context for what it contains; the parser actions, made static.
ENTER = dict(
	call=lambda c: c._replace(F=1, P=0),
	brace=lambda c: c._replace(F=0, B=1, P=0),
	bracket=lambda c: c._replace(P=2) if c.P else c,
	name=lambda c: c._replace(K=1, P=2 if c.P else 0),
	group=lambda c: c._replace(P=1) if c.G else c,
)

ENTRY_MODES = ('plain', 'cmd', 'args', 'eq')


def contexts():
	starts = {(m, g): Ctx(m, 0, 0, 0, 0, g) for m in ENTRY_MODES for g in (0, 1)}
	seen, todo = set(), list(starts.values())
	while todo:
		c = todo.pop()
		if c not in seen:
			seen.add(c)
			todo.extend(enter(c) for enter in ENTER.values())
	return starts, seen


def merge(seen):
	"""Classes of contexts that read every token alike and lead to alike contexts."""
	part = {c: (text_tokens(c), c.F, c.P, c.G) for c in seen}
	while True:
		key = {c: (part[c], tuple(part[enter(c)] for enter in ENTER.values())) for c in seen}
		ids = {}
		refined = {c: ids.setdefault(k, len(ids)) for c, k in sorted(key.items())}
		stable = len(set(refined.values())) == len(set(part.values()))
		part = refined
		if stable:
			return part


def order(c):
	return (c.G, c.F, c.P, c.K, c.mode, c.B)


def names_for(seen, part):
	representative = {}
	for c in sorted(seen, key=order):
		representative.setdefault(part[c], c)
	names, used = {}, {}
	for cls, c in sorted(representative.items(), key=lambda kv: order(kv[1])):
		base = (('Call' if c.F else 'Top')
				+ ('Group' if c.P == 1 else 'InGroup' if c.P == 2 else '')
				+ '_' + (''.join(LETTER[t] for t in text_tokens(c)) or 'none')
				+ ('_G' if c.G else ''))
		n = used.get(base, 0)
		used[base] = n + 1
		names[cls] = base if n == 0 else f'{base}{n + 1}'
	return representative, names


def describe(c):
	where = ['inside a call' if c.F else 'outside a call']
	if c.P == 1:
		where.append('inside a paren group')
	if c.P == 2:
		where.append("in brackets or a name inside a paren group")
	if c.K:
		where.append('inside a register name')
	if c.G:
		where.append('paren_groups on')
	return f"{', '.join(where)}; text: {' '.join(SHOWN[t] for t in text_tokens(c)) or 'none of , ) ; = >'}"


def generate(grammar_name):
	starts, seen = contexts()
	part = merge(seen)
	representative, names = names_for(seen, part)

	def N(c):
		return names[part[c]]

	def inside(c, construct):
		return N(ENTER[construct](c))

	def first(c):
		alts = [f'bracePattern__{inside(c, "brace")}', f'bracketPattern__{inside(c, "bracket")}', f'beginGenericText__{N(c)}',
				f'PERCENT validSubstitution__{inside(c, "name")}', f'regexpCapture__{inside(c, "name")}']
		if c.G:
			alts.append(f'closedGroupFirst__{inside(c, "group")}')
		return alts

	def later(c):
		alts = [f'bracePattern__{inside(c, "brace")}', f'bracketPattern__{inside(c, "bracket")}',
				f'PERCENT validSubstitution__{inside(c, "name")}', f'regexpCapture__{inside(c, "name")}', f'genericText__{N(c)}']
		if c.G:
			alts.append(f'closedGroup__{inside(c, "group")}')
		return alts

	def block(alts, indent='        '):
		return '(\n' + '\n'.join(f'{indent}{"  " if i == 0 else "| "}{a}' for i, a in enumerate(alts)) + '\n    )'

	out = []
	w = out.append
	w('// GENERATED by tools/grammar/generate-context-parser.py from the predicates of')
	w('// SharpMUSH.Parser.Generated/SharpMUSHParser.g4. Edit the generator, not this file.')
	w('//')
	w('// The same language as SharpMUSHParser.g4, with no predicates and no parser state. Every rule that')
	w('// can see a , ) ; = or > exists once per context, named rule__Context: Top_ or Call_ (outside or')
	w('// inside a function call), Group/InGroup (paren_groups), then the letters of the tokens that are')
	w('// text there (R ")", S ";", C ",", E "=", A ">"), then _G when paren_groups is on.')
	w(f'parser grammar {grammar_name};\n')
	w('options {\n    tokenVocab = SharpMUSHLexer;\n}\n')

	entries = [
		('startSingleCommandString', lambda g: f'command__{N(starts["plain", g])} EOF'),
		('startCommandString', lambda g: f'commandList__{N(starts["cmd", g])} EOF'),
		('startPlainCommaCommandArgs', lambda g: f'commaCommandArgs__{N(starts["args", g])} EOF'),
		('startEqSplitCommandArgs', lambda g: f'evaluationString__{N(starts["eq", g])}? (EQUALS commaCommandArgs__{N(starts["args", g])})? EOF'),
		('startEqSplitCommand', lambda g: f'evaluationString__{N(starts["eq", g])}? (EQUALS evaluationString__{N(starts["plain", g])}?)? EOF'),
		('startPlainSingleCommandArg', lambda g: f'evaluationString__{N(starts["plain", g])}? EOF'),
		('startPlainString', lambda g: f'evaluationString__{N(starts["plain", g])} EOF'),
	]
	w('// Entry points; the __G ones read code with paren_groups on.')
	for name, body in entries:
		w(f'{name}: {body(0)};')
		w(f'{name}__G: {body(1)};')
	w('')
	for g in (0, 1):
		n = N(starts['cmd', g])
		w(f'commandList__{n}: command__{n} (SEMICOLON command__{n}?)*;')
	for n in sorted({N(starts[m, g]) for m in ('plain', 'cmd') for g in (0, 1)}):
		w(f'command__{n}: evaluationString__{n};')
	for g in (0, 1):
		n = N(starts['args', g])
		if g:
			w('// An argument followed by a comma cannot end inside an open paren group, which would take the comma.')
			w(f'commaCommandArgs__{n}: (evaluationString__{n}_closed? COMMAWS)* evaluationString__{n}?;')
		else:
			w(f'commaCommandArgs__{n}: evaluationString__{n}? (COMMAWS evaluationString__{n}?)*;')
	w('')

	for c in sorted(representative.values(), key=N):
		n = N(c)
		group = inside(c, 'group')
		# An unclosed group runs to the end of the text it is in. Inside a call that end is the call's
		# ')', which the group would have taken, so a call's arguments have only closed groups.
		open_tails = c.G and not c.F
		w(f'// ---- {n}: {describe(c)}')
		w(f'evaluationString__{n}:\n      function__{inside(c, "call")} explicitEvaluationString__{n}?\n    | explicitEvaluationString__{n}\n;')
		loop = block(later(c)) + '*'
		if open_tails:
			w(f'explicitEvaluationString__{n}:\n      {block(first(c))} {loop} openTail__{group}?\n    | openTailFirst__{group}\n;')
			w(f'braceExplicitEvaluationString__{n}:\n      {block(later(c))} {loop} openTail__{group}?\n    | openTail__{group}\n;')
		else:
			w(f'explicitEvaluationString__{n}:\n    {block(first(c))} {loop}\n;')
			w(f'braceExplicitEvaluationString__{n}:\n    {block(later(c))} {loop}\n;')
		if c == starts['args', 1]:
			w(f'evaluationString__{n}_closed:\n      function__{inside(c, "call")} explicitEvaluationString__{n}_closed?\n    | explicitEvaluationString__{n}_closed\n;')
			w(f'explicitEvaluationString__{n}_closed:\n    {block(first(c))} {loop}\n;')
		w(f'function__{n}: FUNCHAR (evaluationString__{n}? (COMMAWS evaluationString__{n}?)*)? CPAREN;')
		w(f'bracePattern__{n}: OBRACE braceExplicitEvaluationString__{n}? CBRACE;')
		w(f'bracketPattern__{n}: OBRACK evaluationString__{n} CBRACK;')
		w(f'regexpCapture__{n}: REGEXP_NUM | REGEXP_STARTCARET explicitEvaluationString__{n}? CCARET?;')
		w(f'validSubstitution__{n}: complexSubstitutionSymbol__{n} | substitutionSymbol;')
		w(f'complexSubstitutionSymbol__{n}: (REG_STARTCARET explicitEvaluationString__{n} CCARET | REG_NUM | REG_ALPHA | ITEXT_NUM | ITEXT_LAST | STEXT_NUM | STEXT_LAST | VWX);')
		# With paren_groups, a '(' or a name's '(' that starts no call opens a group instead of being text.
		w(f'genericText__{n}: beginGenericText__{n}' + ('' if c.G else ' | FUNCHAR') + ';')
		tokens = list(text_tokens(c)) + ([] if c.G else ['OPAREN'])
		w(f'beginGenericText__{n}: ' + ' | '.join(tokens + ['(escapedText | OTHER | DOLLAR | ansi)']) + ';')
		if c.P == 1:
			body = block(later(c)) + '*'
			w(f'closedGroupFirst__{n}: beginGenericText__Open {body} genericText__Close;')
			w(f'closedGroup__{n}: genericText__Open {body} genericText__Close;')
			if not c.F:
				w(f'openTailFirst__{n}: beginGenericText__Open {body} openTail__{n}?;')
				w(f'openTail__{n}: genericText__Open {body} openTail__{n}?;')
		w('')

	w('genericText__Open: beginGenericText__Open | FUNCHAR;')
	w('beginGenericText__Open: OPAREN;')
	w('genericText__Close: beginGenericText__Close;')
	w('beginGenericText__Close: CPAREN;\n')
	w('''substitutionSymbol: (
        SPACE | BLANKLINE | TAB | COLON | DBREF | ENACTOR_NAME | CAP_ENACTOR_NAME | ACCENT_NAME | MONIKER_NAME
        | PERCENT | SUB_PRONOUN | OBJ_PRONOUN | POS_PRONOUN | ABS_POS_PRONOUN | ARG_NUM | CALLED_DBREF
        | EXECUTOR_DBREF | LOCATION_DBREF | LASTCOMMAND_BEFORE_EVAL | LASTCOMMAND_AFTER_EVAL | PIPED_OUTPUT
        | INVOCATION_DEPTH | EQUALS | CURRENT_ARG_COUNT | OTHER_SUB
    )
;

escapedText: ESCAPE ANY;

ansi: OANSI ANSICHARACTER? CANSI;''')

	return prune('\n'.join(out), [n for n, _ in entries] + [n + '__G' for n, _ in entries]), len(seen), len(representative)


RULE = re.compile(r'([a-z][A-Za-z_0-9]*)\s*:')


def prune(grammar, entries):
	"""Drops the rules no entry point reaches, and context headers left with no rules."""
	blocks = re.split(r'\n(?=[a-zA-Z/])', grammar)
	rules = {m.group(1): b for b in blocks if (m := RULE.match(b))}
	reached, todo = set(), list(entries)
	while todo:
		r = todo.pop()
		if r in reached or r not in rules:
			continue
		reached.add(r)
		body = re.sub(r'//[^\n]*', '', rules[r].split(':', 1)[1])
		todo.extend(re.findall(r'\b([a-z][A-Za-z_0-9]*)\b', body))
	kept = [b for b in blocks if not ((m := RULE.match(b)) and m.group(1) not in reached)]
	return '\n'.join(b for i, b in enumerate(kept)
		if not (b.startswith('// ----') and (i + 1 == len(kept) or kept[i + 1].startswith('// ----') or not kept[i + 1].strip())))


if __name__ == '__main__':
	args = argparse.ArgumentParser(description=__doc__.splitlines()[0])
	args.add_argument('--name', default='SharpMUSHContextParser', help='the grammar name to write')
	grammar, contexts_seen, classes = generate(args.parse_args().name)
	print(grammar)
	print(f'{contexts_seen} contexts, {classes} after merging, {len(re.findall(r'^[a-z][A-Za-z_0-9]*\s*:', grammar, re.M))} rules', file=sys.stderr)
