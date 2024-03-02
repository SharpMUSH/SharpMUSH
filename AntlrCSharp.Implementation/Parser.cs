using Antlr4.Runtime;
using AntlrCSharp.Implementation.Definitions;
using AntlrCSharp.Implementation.Visitors;

namespace AntlrCSharp.Implementation
{
    /// <summary>
    /// Provides the parser.
    /// Each call is Synchronous, and stateful at this time.
    /// </summary>
    public class Parser
	{
		public struct ParserState
		{
			Dictionary<string, MString> Registers;
			DBAttribute CurrentEvaluation;
			MString[] Arguments;
			DBref Executor;
			DBref Enactor;
			DBref Caller;
		}

		/// <summary>
		/// Stack may not be needed if we can bring ParserState into the custom Visitors.
		/// 
		/// Stack should be good enough, since we parse left-to-right when we consider the Visitors.
		/// However, we may run into issues when it comes to function-depth calculations.
		/// 
		/// Time to start drawing a tree to make sure we put things in the right spots.
		/// </summary>
		public Stack<ParserState> State { get; set; }

		public Parser()
		{
			State = new();
		}

		public Parser(Stack<ParserState>? state)
		{
			State = state ?? new();
		}

		public Parser(Parser parser, ParserState state) : this(null)
		{
			foreach(var st in parser.State.ToArray().Reverse())
			{
				State.Push(st);
			}
			
			State.Push(state);
		}

		public CallState? FunctionParse(string text)
		{
			AntlrInputStream inputStream = new(text);
			PennMUSHLexer pennLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(pennLexer);
			PennMUSHParser pennParser = new(commonTokenStream);
			PennMUSHParser.EvaluationStringContext chatContext = pennParser.evaluationString();
			PennMUSHParserVisitor visitor = new(this);

			return visitor.Visit(chatContext);
		}

		public CallState? CommandListParse(string text)
		{
			AntlrInputStream inputStream = new(text);
			PennMUSHLexer pennLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(pennLexer);
			PennMUSHParser pennParser = new(commonTokenStream);
			PennMUSHParser.CommandListContext chatContext = pennParser.commandList();
			PennMUSHParserVisitor visitor = new(this);

			return visitor.Visit(chatContext);
		}

		public CallState? CommandParse(string text)
		{
			AntlrInputStream inputStream = new(text);
			PennMUSHLexer pennLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(pennLexer);
			PennMUSHParser pennParser = new(commonTokenStream);
			PennMUSHParser.CommandContext chatContext = pennParser.command();
			PennMUSHParserVisitor visitor = new(this);

			return visitor.Visit(chatContext);
		}
	}
}