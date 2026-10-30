using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Parsing;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// A command whose name is a built-in has its arguments split in the parse of the line: the pipeline
/// puts a <c>CMD_*</c> token in place of the name and switches, and lexes the arguments as their own text.
/// </summary>
public class CommandHeadTests
{
	/// <summary>think takes one argument, @pemit splits at '=', @trigger at '=' then ','.</summary>
	private sealed class Heads : ICommandHeads
	{
		public int SplitToken(string name) => name.ToUpperInvariant() switch
		{
			"THINK" or "LOOK" => SharpMUSHLexer.CMD_SINGLE,
			"@PEMIT" or "@PEMIT/SILENT" => SharpMUSHLexer.CMD_EQ,
			"@TRIGGER" => SharpMUSHLexer.CMD_EQ_ARGS,
			"@DOLIST" => SharpMUSHLexer.CMD_EQ,
			_ => 0
		};
	}

	private static (string Tokens, SharpMUSHParser.ICommandContext[] Commands, int Errors) Parse(string input, bool list)
	{
		var tokens = SoftcodeParsePipeline.Lex(input, nameof(CommandHeadTests), heads: new Heads(), commandList: list);
		var names = string.Join(' ', tokens.tokens.Select(t => SharpMUSHLexer.DefaultVocabulary.GetSymbolicName(t.Type)));
		var parser = SoftcodeParsePipeline.CreateParser(tokens, parenGroups: false, PredictionMode.LL);
		var listener = new ParserErrorListener(input);
		parser.AddErrorListener(listener);
		var tree = SoftcodeParsePipeline.Enter(parser, list ? ParseType.CommandList : ParseType.Command);
		var commands = tree is SharpMUSHParser.IStartCommandStringContext start
			? start.commandList().command()
			: [((SharpMUSHParser.IStartSingleCommandStringContext)tree).command()];
		return (names, commands, listener.Errors.Count);
	}

	[Test]
	public async Task FunctionAfterTheNameIsACall()
	{
		var (tokens, commands, errors) = Parse("think add(1,2)", list: false);

		await Assert.That(errors).IsEqualTo(0);
		await Assert.That(tokens).IsEqualTo("CMD_SINGLE FUNCHAR OTHER COMMAWS OTHER CPAREN EOF");
		await Assert.That(commands[0].evaluationString(0).function()).IsNotNull();
	}

	[Test]
	public async Task HeadTakesTheSpacesAfterTheName()
	{
		var (tokens, commands, _) = Parse("@pemit/silent   me=Hello World", list: false);

		await Assert.That(tokens).IsEqualTo("CMD_EQ OTHER EQUALS OTHER EOF");
		await Assert.That(commands[0].Start.Text).IsEqualTo("@pemit/silent   ");
		await Assert.That(commands[0].evaluationString(0).GetText()).IsEqualTo("me");
	}

	[Test]
	public async Task EachCommandOfAListIsMarked()
	{
		var (_, commands, errors) = Parse("@pemit me=a;think [add(1,2)];say hi;look", list: true);

		await Assert.That(errors).IsEqualTo(0);
		await Assert.That(commands.Select(c => c.Start.Type)).IsEquivalentTo(
			[SharpMUSHLexer.CMD_EQ, SharpMUSHLexer.CMD_SINGLE, SharpMUSHLexer.OTHER, SharpMUSHLexer.CMD_SINGLE]);
	}

	[Test]
	public async Task SemicolonInsideABraceStartsNoCommand()
	{
		var (_, commands, errors) = Parse("@dolist a b={think ##;think x}", list: true);

		await Assert.That(errors).IsEqualTo(0);
		await Assert.That(commands.Length).IsEqualTo(1);
		await Assert.That(commands[0].Start.Type).IsEqualTo(SharpMUSHLexer.CMD_EQ);
	}

	[Test]
	public async Task EqSplitArgsSplitsTheRightSideOnCommas()
	{
		var (_, commands, errors) = Parse("@trigger me/x=a,[add(1,2)],c;think %0", list: true);

		await Assert.That(errors).IsEqualTo(0);
		await Assert.That(commands[0].commaCommandArgs().evaluationString().Length).IsEqualTo(3);
	}

	[Test]
	[Arguments("addcom=Public")]
	[Arguments("think[add(1,2)]")]
	[Arguments("[think]")]
	[Arguments("say hi")]
	public async Task OtherCommandsStayOneString(string input)
	{
		var (_, commands, _) = Parse(input, list: false);

		await Assert.That(commands[0].Start.Type).IsNotEqualTo(SharpMUSHLexer.CMD_SINGLE)
			.And.IsNotEqualTo(SharpMUSHLexer.CMD_EQ);
	}

	[Test]
	public async Task TokensAfterTheArgumentStartAreNumberedInOrder()
	{
		var tokens = SoftcodeParsePipeline.Lex("think %# x;think y", nameof(CommandHeadTests), heads: new Heads(),
			commandList: true);

		for (var i = 0; i < tokens.tokens.Count; i++)
		{
			await Assert.That(tokens.tokens[i].TokenIndex).IsEqualTo(i);
		}
	}
}
