using System.Diagnostics;
using System.Reflection;
using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using ContextLexer = SharpMUSH.Tools.Grammar.SharpMUSHLexer;
using ContextParser = SharpMUSH.Tools.Grammar.SharpMUSHContextParser;

// dotnet run --project tools/grammar/compare -- [--fuzz N] [--show N] [--bench] [--scale TEXT] FILE...
// Each FILE holds one input per line.
var files = new List<string>();
int fuzz = 0, show = 20;
string? mode = null, scale = null;
for (var i = 0; i < args.Length; i++)
{
	switch (args[i])
	{
		case "--fuzz": fuzz = int.Parse(args[++i]); break;
		case "--show": show = int.Parse(args[++i]); break;
		case "--bench": mode = "bench"; break;
		case "--scale": mode = "scale"; scale = args[++i]; break;
		default: files.Add(args[i]); break;
	}
}

var inputs = files.SelectMany(File.ReadAllLines).Where(line => line.Length is > 0 and < 2000).ToList();
var random = new Random(1);
string[] pieces = ["a(", "bc(", ")", ",", "[", "]", "{", "}", "%", "%0", "%q<", ">", ";", "=", "^", "\\", "$", "$<", "x", " ", "(", "%#", "%r", "$1", "\u001b[", "m", "y"];
for (var i = 0; i < fuzz; i++)
{
	inputs.Add(string.Concat(Enumerable.Range(0, random.Next(1, 24)).Select(_ => pieces[random.Next(pieces.Length)])));
}

string[] entries = ["startPlainString", "startCommandString", "startSingleCommandString", "startPlainCommaCommandArgs", "startEqSplitCommandArgs", "startEqSplitCommand", "startPlainSingleCommandArg"];

switch (mode)
{
	case "bench": Bench(); return;
	case "scale": Scale(scale!); return;
}

int runs = 0, same = 0, bothFail = 0, treeDiff = 0, currentOnly = 0, contextOnly = 0, shown = 0;
var clock = Stopwatch.StartNew();
foreach (var text in inputs)
{
	foreach (var entry in entries)
	{
		foreach (var groups in new[] { false, true })
		{
			runs++;
			var current = ParseCurrent(text, entry, groups);
			var context = ParseContext(text, entry, groups);
			string kind;
			switch (current.Errors.Count == 0, context.Errors.Count == 0)
			{
				case (true, true) when current.Tree == context.Tree: same++; continue;
				case (true, true): treeDiff++; kind = "TREE"; break;
				case (true, false): currentOnly++; kind = "ONLY-CURRENT-PARSES"; break;
				case (false, true): contextOnly++; kind = "ONLY-CONTEXT-PARSES"; break;
				default: bothFail++; continue;
			}

			if (shown++ < show)
			{
				Console.WriteLine($"{kind} {entry} paren_groups={groups}: {text}\n  current: {current.Tree} {string.Join("; ", current.Errors)}\n  context: {context.Tree} {string.Join("; ", context.Errors)}");
			}
		}
	}
}

Console.WriteLine($"{inputs.Count} inputs, {runs} parses in {clock.Elapsed.TotalSeconds:F0}s: {same} same tree, {bothFail} rejected by both, {treeDiff} different trees, {currentOnly} parsed only by the current grammar, {contextOnly} only by the context grammar");
return;

// The current parser as SharpMUSH runs it: SLL with predicates resolved where each decision starts,
// then plain LL when that fails, which is where errors come from.
static (string Tree, List<string> Errors) ParseCurrent(string text, string entry, bool groups)
{
	foreach (var sll in new[] { true, false })
	{
		var parser = new SharpMUSHParser(Lex(text));
		if (sll)
		{
			parser.ResolvePredicatesAtDecisionStart();
		}

		parser.parenGroups = groups;
		var errors = Prepare(parser, sll);
		try
		{
			var tree = (ParserRuleContext)typeof(SharpMUSHParser).GetMethod(entry, Type.EmptyTypes)!.Invoke(parser, null)!;
			if (!sll || errors.Count == 0)
			{
				return (Write(tree, parser.RuleNames), errors);
			}
		}
		catch (TargetInvocationException e) when (e.InnerException is ParseCanceledException)
		{
		}
	}

	throw new UnreachableException();
}

// The generated grammar the same way, without anything resolved by hand: SLL, then LL.
static (string Tree, List<string> Errors) ParseContext(string text, string entry, bool groups)
{
	foreach (var sll in new[] { true, false })
	{
		var parser = new ContextParser(Lex(text));
		var errors = Prepare(parser, sll);
		try
		{
			var tree = (ParserRuleContext)typeof(ContextParser).GetMethod(groups ? entry + "__G" : entry, Type.EmptyTypes)!.Invoke(parser, null)!;
			if (!sll || errors.Count == 0)
			{
				return (Write(tree, parser.RuleNames), errors);
			}
		}
		catch (TargetInvocationException e) when (e.InnerException is ParseCanceledException)
		{
		}
	}

	throw new UnreachableException();
}

static CommonTokenStream Lex(string text)
{
	var lexer = new ContextLexer(new AntlrInputStream(text));
	lexer.RemoveErrorListeners();
	var tokens = new CommonTokenStream(lexer);
	tokens.Fill();
	return tokens;
}

static List<string> Prepare(Parser parser, bool sll)
{
	parser.Interpreter.PredictionMode = sll ? PredictionMode.SLL : PredictionMode.LL;
	parser.RemoveErrorListeners();
	var errors = new List<string>();
	parser.AddErrorListener(new Errors(errors));
	if (sll)
	{
		parser.ErrorHandler = new BailErrorStrategy();
	}

	return errors;
}

// A tree as Trees.ToStringTree writes it, with each rule__Context named by its rule and the paren
// group rules, which the current grammar has no node for, replaced by their children.
static string Write(IParseTree tree, string[] ruleNames)
{
	var text = new StringBuilder();
	Visit(tree);
	return text.ToString();

	void Visit(IParseTree node)
	{
		if (node is ITerminalNode terminal)
		{
			text.Append(' ').Append(Trees.GetNodeText(terminal, ruleNames).Replace("\\", "\\\\").Replace(" ", "\\s").Replace("(", "\\(").Replace(")", "\\)"));
			return;
		}

		var rule = ruleNames[((RuleContext)node).RuleIndex];
		var cut = rule.IndexOf("__", StringComparison.Ordinal);
		var name = cut < 0 ? rule : rule[..cut];
		var group = name is "closedGroup" or "closedGroupFirst" or "openTail" or "openTailFirst";
		if (!group)
		{
			text.Append(" (").Append(name);
		}

		for (var i = 0; i < node.ChildCount; i++)
		{
			Visit(node.GetChild(i));
		}

		if (!group)
		{
			text.Append(')');
		}
	}
}

// Parses every input as a command list, four times over, with each grammar's SLL pass.
void Bench()
{
	var streams = inputs.Select(Lex).ToArray();
	for (var round = 1; round <= 4; round++)
	{
		foreach (var groups in new[] { false, true })
		{
			var clock = Stopwatch.StartNew();
			foreach (var tokens in streams)
			{
				tokens.Seek(0);
				var parser = new SharpMUSHParser(tokens);
				parser.ResolvePredicatesAtDecisionStart();
				parser.parenGroups = groups;
				Prepare(parser, sll: true);
				try { parser.startCommandString(); } catch (ParseCanceledException) { }
			}

			var current = clock.Elapsed.TotalMilliseconds;
			clock.Restart();
			foreach (var tokens in streams)
			{
				tokens.Seek(0);
				var parser = new ContextParser(tokens);
				Prepare(parser, sll: true);
				try
				{
					if (groups) { parser.startCommandString__G(); } else { parser.startCommandString(); }
				}
				catch (ParseCanceledException) { }
			}

			Console.WriteLine($"round {round}, paren_groups={groups}: current {current:F0}ms, context {clock.Elapsed.TotalMilliseconds:F0}ms for {streams.Length} inputs");
		}
	}
}

// Times TEXT repeated n times, first run and second, under both grammars with paren_groups on and off.
void Scale(string piece)
{
	foreach (var n in new[] { 25, 50, 100, 200, 400 })
	{
		var text = string.Concat(Enumerable.Repeat(piece, n));
		foreach (var groups in new[] { false, true })
		{
			var line = new StringBuilder($"n={n} paren_groups={groups}:");
			foreach (var (name, parse) in new (string, Func<(string, List<string>)>)[] { ("context", () => ParseContext(text, "startPlainString", groups)), ("current", () => ParseCurrent(text, "startPlainString", groups)) })
			{
				var clock = Stopwatch.StartNew();
				parse();
				var first = clock.ElapsedMilliseconds;
				clock.Restart();
				parse();
				line.Append($" {name} {first}/{clock.ElapsedMilliseconds}ms");
			}

			Console.WriteLine(line);
		}
	}
}

internal sealed class Errors(List<string> errors) : BaseErrorListener
{
	public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
		=> errors.Add($"{line}:{charPositionInLine} {msg}");
}
