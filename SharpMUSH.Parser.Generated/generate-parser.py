#!/usr/bin/env python3
"""Writes the softcode parser grammar, SharpMUSHParser.g4, and its C# bridge, SharpMUSHParser.Contexts.cs.

Whether ',', ')', ';', '=' and '>' are text or structure depends on where they are: a comma ends a
function argument inside a call and is text outside one. The grammar has no predicates and no
parser state for that. Each rule exists once per context that changes the answer, and its name
says which: a comma is a separator in function__Call_* rules and text in Top_* ones because the
rules say so. ANTLR settles a predicate only after its lookahead reaches a conflict, so with
predicates a bracketed argument full of calls took time exponential in the calls; with the
context in the rule names, prediction settles each token where it stands.

The bridge gives each family of copies one interface (IFunctionContext for every function__*), a
visitor that sends each copy to one method, and flattens paren groups into their parent so the
tree has the shape the evaluator reads.

Usage: python3 SharpMUSH.Parser.Generated/generate-parser.py      (rewrites both files)
       python3 SharpMUSH.Parser.Generated/generate-parser.py --check   (fails if either is stale)
See docs/design/softcode-grammar.md.
"""
import argparse
import re
import sys
from collections import namedtuple

# Where a token stands, as the facts that decide what it is.
#   mode  the entry point: plain | cmd (command list, ';' separates) | args (',' separates)
#         | eq (before the '=' of an '=' split)
#   F     inside a function call, since the last brace (a brace starts over)
#   B     inside a brace, at any depth
#   K     inside a register or capture name (%q<...>, $<...>), where '>' ends the name
#   P     paren groups (paren_groups only): 0 none, 1 inside a group, whose ')' closes it,
#         2 in brackets or a name inside a group, where ')' is text and ',' is still text
#   G     paren_groups is on
Ctx = namedtuple('Ctx', 'mode F B K P G')

LETTER = dict(CPAREN='R', SEMICOLON='S', COMMAWS='C', EQUALS='E', CCARET='A')
SHOWN = dict(CPAREN="')'", SEMICOLON="';'", COMMAWS="','", EQUALS="'='", CCARET="'>'")


def text_tokens(c):
	"""The structural tokens that are plain text (beginGenericText) in context c."""
	tokens = []
	if c.P == 2 or (not c.F and not c.P):  # no call or group for it to close
		tokens.append('CPAREN')
	if c.mode != 'cmd' or c.B:  # not separating commands
		tokens.append('SEMICOLON')
	if c.P or (not c.F and (c.mode != 'args' or c.B)):  # not separating arguments
		tokens.append('COMMAWS')
	if c.mode != 'eq' or c.F:  # not the '=' that splits a command
		tokens.append('EQUALS')
	if not c.K:  # not ending a register name
		tokens.append('CCARET')
	return tuple(tokens)


# How each construct changes the context for what it contains.
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
	w('// GENERATED by generate-parser.py. Edit the generator, not this file; see docs/design/softcode-grammar.md.')
	w('//')
	w('// No predicates and no parser state. Every rule that can see a , ) ; = or > exists once per')
	w('// context, named rule__Context: Top_ or Call_ (outside or inside a function call), Group/InGroup')
	w('// (paren_groups), then the letters of the tokens that are text there (R ")", S ";", C ",", E "=",')
	w('// A ">"), then _G when paren_groups is on.')
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
		w(f'function__{n}: FUNCHAR evaluationString__{n}? (COMMAWS evaluationString__{n}?)* CPAREN;')
		w(f'bracePattern__{n}: OBRACE braceExplicitEvaluationString__{n}? CBRACE;')
		w(f'bracketPattern__{n}: OBRACK evaluationString__{n}? CBRACK;')
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


# What each rule's context exposes, as the evaluator reads it: (accessor, element, many). An element
# in lower case is a rule; with copies its type is the family's interface, without them the
# generated class. One in upper case is a token. many: the element can occur more than once, so it
# has an array accessor and an indexed one, as ANTLR writes them.
ACCESSORS = {
	'startSingleCommandString': [('command', False), ('Eof', False)],
	'startCommandString': [('commandList', False), ('Eof', False)],
	'startPlainCommaCommandArgs': [('commaCommandArgs', False), ('Eof', False)],
	'startEqSplitCommandArgs': [('evaluationString', False), ('EQUALS', False), ('commaCommandArgs', False), ('Eof', False)],
	'startEqSplitCommand': [('evaluationString', True), ('EQUALS', False), ('Eof', False)],
	'startPlainSingleCommandArg': [('evaluationString', False), ('Eof', False)],
	'startPlainString': [('evaluationString', False), ('Eof', False)],
	'commandList': [('command', True), ('SEMICOLON', True)],
	'command': [('evaluationString', False)],
	'commaCommandArgs': [('evaluationString', True), ('COMMAWS', True)],
	'evaluationString': [('function', False), ('explicitEvaluationString', False)],
	'explicitEvaluationString': [('bracePattern', True), ('bracketPattern', True), ('beginGenericText', False),
		('PERCENT', True), ('validSubstitution', True), ('regexpCapture', True), ('genericText', True)],
	'braceExplicitEvaluationString': [('bracePattern', True), ('bracketPattern', True), ('genericText', True),
		('PERCENT', True), ('validSubstitution', True), ('regexpCapture', True)],
	'regexpCapture': [('REGEXP_NUM', False), ('REGEXP_STARTCARET', False), ('explicitEvaluationString', False), ('CCARET', False)],
	'bracePattern': [('OBRACE', False), ('braceExplicitEvaluationString', False), ('CBRACE', False)],
	'bracketPattern': [('OBRACK', False), ('evaluationString', False), ('CBRACK', False)],
	'function': [('FUNCHAR', False), ('evaluationString', True), ('COMMAWS', True), ('CPAREN', False)],
	'validSubstitution': [('complexSubstitutionSymbol', False), ('substitutionSymbol', False)],
	'complexSubstitutionSymbol': [('REG_STARTCARET', False), ('explicitEvaluationString', False), ('CCARET', False),
		('REG_NUM', False), ('REG_ALPHA', False), ('ITEXT_NUM', False), ('ITEXT_LAST', False), ('STEXT_NUM', False),
		('STEXT_LAST', False), ('VWX', False)],
	'genericText': [('beginGenericText', False), ('FUNCHAR', False)],
	'beginGenericText': [('CPAREN', False), ('SEMICOLON', False), ('COMMAWS', False), ('EQUALS', False), ('CCARET', False),
		('OPAREN', False), ('escapedText', False), ('OTHER', False), ('DOLLAR', False), ('ansi', False)],
}

# Rules that are spliced into their parent when they exit: the tree keeps one flat run of text.
GROUPS = ('closedGroup', 'closedGroupFirst', 'openTail', 'openTailFirst')


def pascal(name):
	return name[0].upper() + name[1:]


def bridge(grammar):
	"""The C# that lets the evaluator read every copy of a rule as one rule."""
	rules = re.findall(r'^([a-z][A-Za-z_0-9]*)\s*:', grammar, re.M)
	families = {}
	for rule in rules:
		families.setdefault(rule.split('__', 1)[0], []).append(rule)

	def iface(base):
		return f'I{pascal(base)}Context'

	def element_type(element):
		return iface(element) if element in ACCESSORS else f'{pascal(element)}Context'

	out = []
	w = out.append
	w('// <auto-generated>')
	w('// GENERATED by generate-parser.py, with SharpMUSHParser.g4. Edit the generator, not this file.')
	w('// </auto-generated>')
	w('#nullable disable')
	w('using Antlr4.Runtime;')
	w('using Antlr4.Runtime.Tree;')
	w('')
	w('public partial class SharpMUSHParser')
	w('{')
	w('	/// <summary>A rule of the softcode grammar, whichever context copy of it was parsed.</summary>')
	w('	public interface ISoftcodeContext : IRuleNode')
	w('	{')
	w('		IToken Start { get; }')
	w('		IToken Stop { get; }')
	w('		int Depth();')
	w('		bool IsEmpty { get; }')
	w('	}')
	w('')
	w('	/// <summary>A paren group, which <see cref="ExitRule"/> splices into its parent.</summary>')
	w('	public interface IParenGroupContext : ISoftcodeContext;')
	w('')
	for base, accessors in ACCESSORS.items():
		w(f'	/// <summary>Every <c>{base}__*</c> rule.</summary>')
		w(f'	public interface {iface(base)} : ISoftcodeContext')
		w('	{')
		for element, many in accessors:
			if element[0].isupper():
				token = f'SharpMUSHParser.{element}'
				if many:
					w(f'		ITerminalNode[] {element}() => SoftcodeTree.Tokens(this, {token});')
					w(f'		ITerminalNode {element}(int i) => SoftcodeTree.Token(this, {token}, i);')
				else:
					w(f'		ITerminalNode {element}() => SoftcodeTree.Token(this, {token}, 0);')
			else:
				t = element_type(element)
				if many:
					w(f'		{t}[] {element}() => SoftcodeTree.Rules<{t}>(this);')
					w(f'		{t} {element}(int i) => SoftcodeTree.Rule<{t}>(this, i);')
				else:
					w(f'		{t} {element}() => SoftcodeTree.Rule<{t}>(this, 0);')
		w('	}')
		w('')
	for base, copies in sorted(families.items()):
		if base in GROUPS:
			for rule in copies:
				w(f'	public partial class {pascal(rule)}Context : IParenGroupContext;')
		elif base in ACCESSORS:
			for rule in copies:
				w(f'	public partial class {pascal(rule)}Context : {iface(base)};')
	for rule in ('substitutionSymbol', 'escapedText', 'ansi'):
		w(f'	public partial class {pascal(rule)}Context : ISoftcodeContext;')
	w('')
	w('	/// <summary>')
	w('	/// Whether the code was written with paren_groups on, which picks the <c>__G</c> entry rules: a bare')
	w('	/// <c>(</c> then opens a group whose <c>,</c>, <c>=</c> and <c>;</c> are text.')
	w('	/// </summary>')
	w('	public bool ParenGroups { get; init; }')
	w('')
	for base in ACCESSORS:
		if base.startswith('start'):
			w(f'	/// <summary>Parses from <c>{base}</c>, or <c>{base}__G</c> under <see cref="ParenGroups"/>.</summary>')
			w(f'	public {iface(base)} {base[0].upper() + base[1:]}() => ParenGroups ? {base}__G() : {base}();')
			w('')
	w('	/// <summary>')
	w('	/// Splices a paren group into its parent as it exits, so its text reads as the run of text it is')
	w('	/// part of, as it did when groups were a counter rather than a rule.')
	w('	/// </summary>')
	w('	public override void ExitRule()')
	w('	{')
	w('		var exiting = Context;')
	w('		base.ExitRule();')
	w('		if (exiting is IParenGroupContext && BuildParseTree && Context is { children: { Count: > 0 } siblings } parent')
	w('			&& ReferenceEquals(siblings[^1], exiting))')
	w('		{')
	w('			siblings.RemoveAt(siblings.Count - 1);')
	w('			foreach (var child in exiting.children ?? [])')
	w('			{')
	w('				switch (child)')
	w('				{')
	w('					case RuleContext rule: rule.Parent = parent; break;')
	w('					case TerminalNodeImpl terminal: terminal.Parent = parent; break;')
	w('				}')
	w('')
	w('				siblings.Add(child);')
	w('			}')
	w('		}')
	w('	}')
	w('}')
	w('')
	w('/// <summary>The children of a softcode rule by type, for the accessors every copy shares.</summary>')
	w('internal static class SoftcodeTree')
	w('{')
	w('	public static T Rule<T>(IParseTree node, int index) where T : class')
	w('	{')
	w('		for (var i = 0; i < node.ChildCount; i++)')
	w('		{')
	w('			if (node.GetChild(i) is T match && index-- == 0)')
	w('			{')
	w('				return match;')
	w('			}')
	w('		}')
	w('')
	w('		return null;')
	w('	}')
	w('')
	w('	public static T[] Rules<T>(IParseTree node) where T : class')
	w('	{')
	w('		var matches = new List<T>();')
	w('		for (var i = 0; i < node.ChildCount; i++)')
	w('		{')
	w('			if (node.GetChild(i) is T match)')
	w('			{')
	w('				matches.Add(match);')
	w('			}')
	w('		}')
	w('')
	w('		return [.. matches];')
	w('	}')
	w('')
	w('	public static ITerminalNode Token(IParseTree node, int type, int index)')
	w('	{')
	w('		for (var i = 0; i < node.ChildCount; i++)')
	w('		{')
	w('			if (node.GetChild(i) is ITerminalNode terminal && terminal.Symbol.Type == type && index-- == 0)')
	w('			{')
	w('				return terminal;')
	w('			}')
	w('		}')
	w('')
	w('		return null;')
	w('	}')
	w('')
	w('	public static ITerminalNode[] Tokens(IParseTree node, int type)')
	w('	{')
	w('		var matches = new List<ITerminalNode>();')
	w('		for (var i = 0; i < node.ChildCount; i++)')
	w('		{')
	w('			if (node.GetChild(i) is ITerminalNode terminal && terminal.Symbol.Type == type)')
	w('			{')
	w('				matches.Add(terminal);')
	w('			}')
	w('		}')
	w('')
	w('		return [.. matches];')
	w('	}')
	w('}')
	w('')
	w('/// <summary>')
	w('/// The generated visitor with every copy of a rule sent to one method: override')
	w('/// <c>VisitFunction(IFunctionContext)</c>, not each <c>VisitFunction__Call_*</c>.')
	w('/// </summary>')
	w('public abstract class SharpMUSHParserRuleVisitor<TResult> : SharpMUSHParserBaseVisitor<TResult>')
	w('{')
	for base in ACCESSORS:
		w(f'	public virtual TResult Visit{pascal(base)}([Antlr4.Runtime.Misc.NotNull] SharpMUSHParser.{iface(base)} context) => VisitChildren(context);')
	w('')
	for base, copies in sorted(families.items()):
		if base in ACCESSORS:
			for rule in copies:
				w(f'	public override TResult Visit{pascal(rule)}([Antlr4.Runtime.Misc.NotNull] SharpMUSHParser.{pascal(rule)}Context context) => Visit{pascal(base)}(context);')
	w('}')
	return '\n'.join(out) + '\n'


if __name__ == '__main__':
	import os
	args = argparse.ArgumentParser(description=__doc__.splitlines()[0])
	args.add_argument('--check', action='store_true', help='fail if the files on disk are not what this writes')
	check = args.parse_args().check
	here = os.path.dirname(os.path.abspath(__file__))
	grammar, contexts_seen, classes = generate('SharpMUSHParser')
	grammar += '\n'
	outputs = {'SharpMUSHParser.g4': grammar, 'SharpMUSHParser.Contexts.cs': bridge(grammar)}
	stale = []
	for name, text in outputs.items():
		path = os.path.join(here, name)
		current = open(path, encoding='utf-8').read() if os.path.exists(path) else None
		if current != text:
			stale.append(name)
			if not check:
				with open(path, 'w', encoding='utf-8', newline='\n') as f:
					f.write(text)
	if check and stale:
		sys.exit(f'stale: {", ".join(stale)}; run python3 SharpMUSH.Parser.Generated/generate-parser.py')
	rules = len(re.findall(r'^[a-z][A-Za-z_0-9]*\s*:', grammar, re.M))
	print(f'{contexts_seen} contexts, {classes} after merging, {rules} rules' + (f'; wrote {", ".join(stale)}' if stale and not check else ''), file=sys.stderr)
