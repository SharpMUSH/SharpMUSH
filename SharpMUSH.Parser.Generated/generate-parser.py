#!/usr/bin/env python3
"""Writes the softcode parser grammar, SharpMUSHParser.g4, and its C# bridge, SharpMUSHParser.Contexts.cs.

Whether ',', ')', ';', '=' and '>' are text or structure depends on where they are: a comma ends a
function argument inside a call and is text outside one. The grammar has no predicates and no
parser state for that. Each rule exists once per context that changes the answer, and its name
says which: a comma is a separator in function__Call_* rules and text in Top_* ones because the
rules say so. ANTLR settles a predicate only after its lookahead reaches a conflict, so with
predicates a bracketed argument full of calls took time exponential in the calls; with the
context in the rule names, prediction settles each token where it stands.

The generator writes every rule for every context, then keeps one copy of each set of copies that
read the same tokens the same way, so a rule has as many copies as there are different ways to
read it.

The bridge gives each family of copies one interface (IFunctionContext for every function__*) and
a visitor that sends each copy to one method. SharpMUSHParser.Tree.cs, written by hand, holds what
does not depend on the grammar.

Usage: python3 SharpMUSH.Parser.Generated/generate-parser.py      (rewrites both files)
       python3 SharpMUSH.Parser.Generated/generate-parser.py --check   (fails if either is stale)
See docs/design/softcode-grammar.md.
"""
import argparse
import os
import re
import sys
from collections import namedtuple

# Where a token stands, as the facts that decide what it is.
#   mode  the entry point: plain | cmd (command list, ';' separates) | args (',' separates)
#         | eq (before the '=' of an '=' split)
#   F     inside a function call, since the last brace (a brace starts over)
#   B     inside a brace, at any depth
#   K     inside a register, argument or capture name (%q<...>, %<...>, $<...>), where '>' ends the name
#   P     inside a paren group (paren_groups only), whose ')' closes it
#   G     paren_groups is on
# A bracket or a name starts over: only its ']' or '>' ends it, so inside one every other token is text
# until a call, brace, group or name says otherwise, as PennMUSH's process_expression reads a '[' with
# PT_BRACKET alone and a name with PT_GT alone.
Ctx = namedtuple('Ctx', 'mode F B K P G')

LETTER = dict(CPAREN='R', SEMICOLON='S', COMMAWS='C', EQUALS='E', CCARET='A')


def text_tokens(c):
	"""The structural tokens that are plain text (beginGenericText) in context c."""
	tokens = []
	if not c.F and not c.P:  # no call or group for it to close
		tokens.append('CPAREN')
	if c.mode != 'cmd' or c.B:  # not separating commands
		tokens.append('SEMICOLON')
	if c.P or (not c.F and (c.mode != 'args' or c.B)):  # not separating arguments
		tokens.append('COMMAWS')
	if c.mode != 'eq' or c.F or c.B:  # not the '=' that splits a command
		tokens.append('EQUALS')
	if not c.K:  # not ending a register name
		tokens.append('CCARET')
	return tokens


# How each construct changes the context for what it contains.
ENTER = dict(
	call=lambda c: c._replace(F=1, P=0),
	brace=lambda c: c._replace(F=0, B=1, P=0),
	bracket=lambda c: Ctx('plain', 0, 0, 0, 0, c.G),
	name=lambda c: Ctx('plain', 0, 0, 1, 0, c.G),
	group=lambda c: c._replace(P=1) if c.G else c,
)


def reachable(starts):
	seen, todo = set(), list(starts)
	while todo:
		c = todo.pop()
		if c not in seen:
			seen.add(c)
			todo.extend(enter(c) for enter in ENTER.values())
	return sorted(seen)


def alternatives(alts):
	return ''.join(f'\n    {":" if i == 0 else "|"} {a}' for i, a in enumerate(alts)) + '\n    ;'


def block(alts):
	return '(\n' + '\n'.join(f'        {"  " if i == 0 else "| "}{a}' for i, a in enumerate(alts)) + '\n    )'


def context_rules(c, ref):
	"""Every rule read in context c, as {name: body}. ref(rule, context) names a rule of a context."""
	inner = {construct: enter(c) for construct, enter in ENTER.items()}

	def own(rule, suffix=''):
		return ref(rule, c) + suffix

	def items(start):
		"""What a string holds; start is what it holds first, where a name( cannot be text."""
		alts = [ref('bracePattern', inner['brace']), ref('bracketPattern', inner['bracket'])]
		alts += [own('beginGenericText')] if start else []
		alts += [f'PERCENT {ref("validSubstitution", inner["name"])}', ref('regexpCapture', inner['name']),
			'iterationToken', 'switchSubject']
		alts += [] if start else [own('genericText')]
		alts += [ref('closedGroupFirst' if start else 'closedGroup', inner['group'])] if c.G else []
		return block(alts)

	first, later = items(True), items(False)
	group = ref('openTail', inner['group'])
	# An unclosed group runs to the end of the text it is in. Inside a call that end is the call's
	# ')', which the group would have taken, so a call's arguments have only closed groups.
	open_tails = c.G and not c.F
	rules = {}

	def strings(suffix, open_tails):
		tail = f' {group}?' if open_tails else ''
		# What follows a leading call is not the start of the string, so it takes what may come later in
		# one: a name( there is text, as it is after any other text. explicitEvaluationString cannot
		# begin with a name(, which is how a string that starts with one is known to start with a call.
		rules[own('evaluationString', suffix)] = alternatives([
			f'{ref("function", inner["call"])} {own("callTail", suffix)}?',
			own('explicitEvaluationString', suffix)])
		rules[own('explicitEvaluationString', suffix)] = alternatives(
			[f'{first} {later}*{tail}'] + ([ref('openTailFirst', inner['group'])] if open_tails else []))
		rules[own('callTail', suffix)] = alternatives([f'{later}+{tail}'] + ([group] if open_tails else []))

	strings('', open_tails)
	# An argument followed by a comma cannot end inside an open paren group, which would take the comma.
	if open_tails and c.mode == 'args':
		strings('_closed', False)
	# A brace holds what a call's tail holds: anything a string may hold after its start, a name( as text.
	rules[own('braceExplicitEvaluationString')] = rules[own('callTail')]
	rules[own('command')] = f': {own("evaluationString")};'
	rules[own('commandList')] = f': {own("command")} (SEMICOLON {own("command")}?)*;'
	rules[own('commaCommandArgs')] = (
		f': ({own("evaluationString", "_closed")}? COMMAWS)* {own("evaluationString")}?;' if open_tails
		else f': {own("evaluationString")}? (COMMAWS {own("evaluationString")}?)*;')
	rules[own('function')] = f': FUNCHAR {own("evaluationString")}? (COMMAWS {own("evaluationString")}?)* CPAREN;'
	rules[own('bracePattern')] = f': OBRACE {own("braceExplicitEvaluationString")}? CBRACE;'
	rules[own('bracketPattern')] = f': OBRACK {own("evaluationString")}? CBRACK;'
	rules[own('regexpCapture')] = f': REGEXP_NUM | REGEXP_STARTCARET {own("explicitEvaluationString")}? CCARET?;'
	rules[own('validSubstitution')] = f': {own("complexSubstitutionSymbol")} | substitutionSymbol;'
	rules[own('complexSubstitutionSymbol')] = alternatives([
		f'(REG_STARTCARET | ARG_STARTCARET | ATTR_STARTCARET | DELIM_STARTCARET) {own("explicitEvaluationString")} CCARET',
		'REG_NUM | REG_ALPHA | VWX',
		'ITEXT_NUM | ITEXT_LAST | ITEXT_POS_NUM | ITEXT_POS_LAST',
		'ARG_DASH_NUM | DTEXT_NUM | DTEXT_LAST | STEXT_NUM | STEXT_LAST'])
	# With paren_groups, a '(' or a name's '(' that starts no call opens a group instead of being text.
	rules[own('genericText')] = f': {own("beginGenericText")}' + ('' if c.G else ' | FUNCHAR') + ';'
	text = text_tokens(c) + ([] if c.G else ['OPAREN'])
	rules[own('beginGenericText')] = ': ' + ' | '.join(text + ['escapedText', 'OTHER', 'DOLLAR', 'HASH', 'PERCENT_AT_END', 'ansi']) + ';'
	if c.P:
		loop = f'{later}*'
		rules[own('closedGroupFirst')] = f': beginGenericText__Open {loop} genericText__Close;'
		rules[own('closedGroup')] = f': genericText__Open {loop} genericText__Close;'
		rules[own('openTailFirst')] = f': beginGenericText__Open {loop} {own("openTail")}?;'
		rules[own('openTail')] = f': genericText__Open {loop} {own("openTail")}?;'
	return rules


ENTRIES = dict(
	startSingleCommandString=lambda s: f'{s("command", "plain")} EOF',
	startCommandString=lambda s: f'{s("commandList", "cmd")} EOF',
	startPlainCommaCommandArgs=lambda s: f'{s("commaCommandArgs", "args")} EOF',
	startEqSplitCommandArgs=lambda s: f'{s("evaluationString", "eq")}? (EQUALS {s("commaCommandArgs", "args")})? EOF',
	startEqSplitCommand=lambda s: f'{s("evaluationString", "eq")}? (EQUALS {s("evaluationString", "plain")}?)? EOF',
	startPlainSingleCommandArg=lambda s: f'{s("evaluationString", "plain")}? EOF',
	startPlainString=lambda s: f'{s("evaluationString", "plain")} EOF',
)

# What every family of copies is, in the order the grammar lists them.
FAMILIES = dict(
	commandList="commands separated by ';'",
	command='one command of a list, or the only one',
	commaCommandArgs="a command's arguments, separated by ','",
	evaluationString='a leading call and its tail, or a string that does not start with one',
	explicitEvaluationString='a string that does not start with a call',
	callTail='what follows a leading call; a name( there is text',
	braceExplicitEvaluationString='what a {} holds',
	function="a call: name( and arguments separated by ',' up to its ')'",
	bracePattern='{...}, read as a string in which a call starts over',
	bracketPattern='[...], read as a plain string whatever is around it',
	regexpCapture='$0-$9 or $<name>',
	validSubstitution="what follows a '%'",
	complexSubstitutionSymbol="a '%' substitution that names something",
	genericText='text, after the start of a string',
	beginGenericText='text, at the start of a string',
	closedGroupFirst='a paren group that starts a string and closes',
	closedGroup='a paren group that closes',
	openTailFirst='a paren group that starts a string and runs to its end',
	openTail='a paren group that runs to the end of the string it is in',
)

FIXED = '''genericText__Open: beginGenericText__Open | FUNCHAR;
beginGenericText__Open: OPAREN;
genericText__Close: beginGenericText__Close;
beginGenericText__Close: CPAREN;

substitutionSymbol
    : SPACE | BLANKLINE | TAB | COLON | DBREF | ENACTOR_NAME | CAP_ENACTOR_NAME | ACCENT_NAME | MONIKER_NAME
    | PERCENT | SUB_PRONOUN | OBJ_PRONOUN | POS_PRONOUN | ABS_POS_PRONOUN | ARG_NUM | CALLED_DBREF
    | EXECUTOR_DBREF | LOCATION_DBREF | LASTCOMMAND_BEFORE_EVAL | LASTCOMMAND_AFTER_EVAL | PIPED_OUTPUT
    | INVOCATION_DEPTH | EQUALS | CURRENT_ARG_COUNT | OTHER_SUB
    ;

escapedText: ESCAPE ANY | ESCAPE_AT_END;

iterationToken: ITER_ELEMENT | ITER_POSITION;

switchSubject: SWITCH_SUBJECT;

ansi: OANSI ANSICHARACTER? CANSI;
'''

REF = re.compile(r'\b([a-z]\w*?)__(\d+)(_closed)?\b')
RULE_NAME = re.compile(r'\b[a-z]\w*\b')


def all_rules():
	"""Every rule of every context, named family__<context number>, and the contexts by number."""
	starts = {(m, g): Ctx(m, 0, 0, 0, 0, g) for m in ('plain', 'cmd', 'args', 'eq') for g in (0, 1)}
	ctxs = reachable(starts.values())
	number = {c: i for i, c in enumerate(ctxs)}

	def ref(rule, c):
		return f'{rule}__{number[c]}'

	rules = {}
	for c in ctxs:
		rules.update(context_rules(c, ref))
	entries = {}
	for name, body in ENTRIES.items():
		for g, suffix in ((0, ''), (1, '__G')):
			entries[name + suffix] = f': {body(lambda rule, mode: ref(rule, starts[mode, g]))};'
	return entries, rules, ctxs


def used(entries, rules):
	"""The rules the entry points reach."""
	reached, todo = set(), list(entries.values())
	while todo:
		for name in RULE_NAME.findall(todo.pop()):
			if name in rules and name not in reached:
				reached.add(name)
				todo.append(rules[name])
	return reached


def merge(rules):
	"""Classes of rules that read every token alike: same family, same body, same classes referred to."""
	cls = {name: name.split('__')[0] for name in rules}
	while True:
		key = {name: (cls[name], REF.sub(lambda m: f'<{cls[m.group(0)]}>', body)) for name, body in rules.items()}
		ids = {}
		refined = {name: ids.setdefault(k, len(ids)) for name, k in sorted(key.items())}
		if len(ids) == len(set(cls.values())):
			return refined
		cls = refined


def label(ctxs):
	"""A copy's name, from what is true of every context it is read in: Top or Call (outside or inside a
	call), Group (inside a paren group), the letters of the tokens that are text there, G with paren_groups."""
	def shared(fact):
		values = {fact(c) for c in ctxs}
		return values.pop() if len(values) == 1 else None

	letters, groups = shared(lambda c: ''.join(LETTER[t] for t in text_tokens(c))), shared(lambda c: c.G)
	assert letters is not None and groups is not None, ctxs
	where = {0: 'Top', 1: 'Call', None: ''}[shared(lambda c: c.F)] + ('Group' if shared(lambda c: c.P) else '')
	return '_'.join(part for part in (where, letters or 'none', 'G' if groups else '') if part)


def generate(grammar_name):
	entries, rules, ctxs = all_rules()
	rules = {name: body for name, body in rules.items() if name in used(entries, rules)}
	cls = merge(rules)
	members = {}
	for name in rules:
		members.setdefault(cls[name], []).append(name)
	names = {}
	for family_rules in members.values():
		family, _, closed = REF.match(family_rules[0]).groups()
		copies = sorted({ctxs[int(REF.match(n).group(2))] for n in family_rules})
		names[cls[family_rules[0]]] = f'{family}__{label(copies)}{closed or ""}'
	taken = {}
	for c in sorted(names, key=names.get):
		n = taken[names[c]] = taken.get(names[c], 0) + 1
		names[c] += '' if n == 1 else str(n)

	def rename(text):
		return REF.sub(lambda m: names[cls[m.group(0)]], text)

	merged = {rename(name): rename(body) for name, body in rules.items()}
	assert {n.split('__')[0] for n in merged} <= FAMILIES.keys(), 'every family needs a FAMILIES entry'

	out = [
		'// GENERATED by generate-parser.py. Edit the generator, not this file; see docs/design/softcode-grammar.md.',
		'//',
		'// No predicates and no parser state. Every rule that can see a , ) ; = or > exists once per way of',
		'// reading them, named rule__Context: Top_ or Call_ (outside or inside a function call; neither when',
		'// it is the same either way), Group (inside a paren group), then the letters of the tokens that are',
		'// text there (R ")", S ";", C ",", E "=", A ">"), then _G when paren_groups is on.',
		f'parser grammar {grammar_name};',
		'',
		'options {\n    tokenVocab = SharpMUSHLexer;\n}',
		'',
		'// Entry points; the __G ones read code with paren_groups on.',
	]
	out += [f'{name}{rename(body)}' for name, body in entries.items()]
	for family, about in FAMILIES.items():
		copies = sorted(n for n in merged if n.split('__')[0] == family)
		if copies:
			out += ['', f'// {family}__*: {about}.']
			out += [f'{n}{merged[n]}' for n in copies]
	out += ['', '// Leaves, the same in every context.', FIXED]
	return '\n'.join(out), len(ctxs)


# Rules that are spliced into their parent when they exit: the tree keeps one flat run of text.
GROUPS = ('closedGroup', 'closedGroupFirst', 'openTail', 'openTailFirst')


def frequencies(body):
	"""What a rule's context exposes, as ANTLR counts it: {element: 1 or 2}, 2 when the element can occur
	more than once (so it has an array accessor). Rules are named by family, EOF as Eof."""
	tokens = re.findall(r'[A-Za-z_]\w*|[()|?*+]', body)
	at = 0

	def peek():
		return tokens[at] if at < len(tokens) else None

	def alternatives():
		nonlocal at
		counts = sequence()
		while peek() == '|':
			at += 1
			other = sequence()
			counts = {e: max(counts.get(e, 0), other.get(e, 0)) for e in {**counts, **other}}
		return counts

	def sequence():
		nonlocal at
		counts = {}
		while peek() not in (None, '|', ')'):
			token = tokens[at]
			at += 1
			if token == '(':
				inner = alternatives()
				at += 1
			else:
				inner = {'Eof' if token == 'EOF' else token.split('__')[0]: 1}
			if peek() in ('?', '*', '+'):
				inner = {e: 2 for e in inner} if tokens[at] != '?' else inner
				at += 1
			counts = {e: min(2, counts.get(e, 0) + inner.get(e, 0)) for e in {**counts, **inner}}
		return counts

	return alternatives()


def pascal(name):
	return name[0].upper() + name[1:]


def bridge(grammar):
	"""The C# that lets the evaluator read every copy of a rule as one rule."""
	rules = dict(re.findall(r'^([a-z]\w*)\s*(:[^;]*);', grammar, re.M))
	families = {}
	for rule in rules:
		families.setdefault(rule.split('__')[0], []).append(rule)
	shared = [f for f, copies in families.items() if f not in GROUPS and copies != [f]]
	leaves = [f for f, copies in families.items() if copies == [f]]

	def iface(family):
		return f'I{pascal(family)}Context'

	out = [
		'// <auto-generated>',
		'// GENERATED by generate-parser.py, with SharpMUSHParser.g4. Edit the generator, not this file.',
		'// </auto-generated>',
		'#nullable disable',
		'using Antlr4.Runtime.Tree;',
		'',
		'public partial class SharpMUSHParser',
		'{',
	]
	for family in shared:
		accessors = {}
		for rule in families[family]:
			for element, n in frequencies(rules[rule][1:]).items():
				if element not in GROUPS:
					accessors[element] = max(accessors.get(element, 0), n)
		out += [f'\t/// <summary>Every <c>{family}__*</c> rule.</summary>', f'\tpublic interface {iface(family)} : ISoftcodeContext', '\t{']
		for element, n in accessors.items():
			if element[0].isupper():
				token = f'SharpMUSHParser.{element}'
				out += ([f'\t\tITerminalNode[] {element}() => SoftcodeTree.Tokens(this, {token});',
					f'\t\tITerminalNode {element}(int i) => SoftcodeTree.Token(this, {token}, i);'] if n > 1
					else [f'\t\tITerminalNode {element}() => SoftcodeTree.Token(this, {token}, 0);'])
			else:
				t = iface(element) if element in shared else f'{pascal(element)}Context'
				out += ([f'\t\t{t}[] {element}() => SoftcodeTree.Rules<{t}>(this);',
					f'\t\t{t} {element}(int i) => SoftcodeTree.Rule<{t}>(this, i);'] if n > 1
					else [f'\t\t{t} {element}() => SoftcodeTree.Rule<{t}>(this, 0);'])
		out += ['\t}', '']
	for family, copies in sorted(families.items()):
		base = 'IParenGroupContext' if family in GROUPS else iface(family) if family in shared else 'ISoftcodeContext'
		out += [f'\tpublic partial class {pascal(rule)}Context : {base};' for rule in copies]
	for family in shared:
		if family.startswith('start'):
			out += ['', f'\t/// <summary>Parses from <c>{family}</c>, or <c>{family}__G</c> under <see cref="ParenGroups"/>.</summary>',
				f'\tpublic {iface(family)} {pascal(family)}() => ParenGroups ? {family}__G() : {family}();']
	out += [
		'}',
		'',
		'/// <summary>',
		'/// The generated visitor with every copy of a rule sent to one method: override',
		'/// <c>VisitFunction(IFunctionContext)</c>, not each <c>VisitFunction__Call_*</c>.',
		'/// </summary>',
		'public abstract class SharpMUSHParserRuleVisitor<TResult> : SharpMUSHParserBaseVisitor<TResult>',
		'{',
	]
	out += [f'\tpublic virtual TResult Visit{pascal(f)}([Antlr4.Runtime.Misc.NotNull] SharpMUSHParser.{iface(f)} context) => VisitChildren(context);'
		for f in shared]
	out.append('')
	out += [f'\tpublic override TResult Visit{pascal(rule)}([Antlr4.Runtime.Misc.NotNull] SharpMUSHParser.{pascal(rule)}Context context) => Visit{pascal(f)}(context);'
		for f, copies in sorted(families.items()) if f in shared for rule in copies]
	out.append('}')
	return '\n'.join(out) + '\n'


def main():
	args = argparse.ArgumentParser(description=__doc__.splitlines()[0])
	args.add_argument('--check', action='store_true', help='fail if the files on disk are not what this writes')
	check = args.parse_args().check
	here = os.path.dirname(os.path.abspath(__file__))
	grammar, contexts = generate('SharpMUSHParser')
	grammar += '\n'
	stale = []
	for name, text in {'SharpMUSHParser.g4': grammar, 'SharpMUSHParser.Contexts.cs': bridge(grammar)}.items():
		path = os.path.join(here, name)
		current = open(path, encoding='utf-8').read() if os.path.exists(path) else None
		if current != text:
			stale.append(name)
			if not check:
				with open(path, 'w', encoding='utf-8', newline='\n') as f:
					f.write(text)
	if check and stale:
		sys.exit(f'stale: {", ".join(stale)}; run python3 SharpMUSH.Parser.Generated/generate-parser.py')
	rules = len(re.findall(r'^[a-z]\w*\s*:', grammar, re.M))
	print(f'{contexts} contexts, {rules} rules' + (f'; wrote {", ".join(stale)}' if stale and not check else ''), file=sys.stderr)


if __name__ == '__main__':
	main()
