using Antlr4.Runtime;
using AntlrCSharp.Implementation.Definitions;
using AntlrCSharp.Implementation.Visitors;
using SharpMUSH.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using System.Collections.Immutable;

namespace AntlrCSharp.Implementation
{
	/// <summary>
	/// Provides the parser.
	/// Each call is Synchronous, and stateful at this time.
	/// </summary>
	public class Parser(
			IPasswordService _passwordService,
			IPermissionService _permissionService,
			ISharpDatabase _database)
	{
		public record ParserState(
			ImmutableDictionary<string, MString> Registers,
			DBAttribute? CurrentEvaluation,
			string? Function,
			string? Command,
			CallState[] Arguments,
			DBRef Executor,
			DBRef Enactor,
			DBRef Caller);

		public IPasswordService PasswordService => _passwordService;
		
		public IPermissionService PermissionService => _permissionService;
		
		public ISharpDatabase Database => _database;

		/// <summary>
		/// Stack may not be needed if we can bring ParserState into the custom Visitors.
		/// 
		/// Stack should be good enough, since we parse left-to-right when we consider the Visitors.
		/// However, we may run into issues when it comes to function-depth calculations.
		/// 
		/// Time to start drawing a tree to make sure we put things in the right spots.
		/// </summary>
		public ImmutableStack<ParserState> State { get; private set; } = [];

		public Parser(
			IPasswordService passwordService,
			IPermissionService permissionService,
			ISharpDatabase database,
			ImmutableStack<ParserState> state) :
				this(passwordService, permissionService, database)
				=> State = state ?? [];

		public Parser Push(ParserState state)
		{
			State = State.Push(state);
			return this;
		}
		
		public Parser Pop()
		{
			State = State.Pop();
			return this;
		}

		public Parser(
			IPasswordService passwordService,
			IPermissionService permissionService,
			ISharpDatabase database,
			ParserState state) :
				this(passwordService, permissionService, database)
				=> State = [state];

		public CallState? FunctionParse(string text)
		{
			AntlrInputStream inputStream = new(text);
			SharpMUSHLexer pennLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(pennLexer);
			SharpMUSHParser pennParser = new(commonTokenStream);
			SharpMUSHParser.PlainStringContext chatContext = pennParser.plainString();
			SharpMUSHParserVisitor visitor = new(this);

			return visitor.Visit(chatContext);
		}

		public CallState? CommandListParse(string text)
		{
			AntlrInputStream inputStream = new(text);
			SharpMUSHLexer pennLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(pennLexer);
			SharpMUSHParser pennParser = new(commonTokenStream);
			SharpMUSHParser.CommandListContext chatContext = pennParser.commandList();
			SharpMUSHParserVisitor visitor = new(this);

			return visitor.Visit(chatContext);
		}

		// TODO: Executor should carry more than just dbref. Also port information.
		public CallState? CommandParse(string text)
		{
			AntlrInputStream inputStream = new(text);
			SharpMUSHLexer pennLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(pennLexer);
			SharpMUSHParser pennParser = new(commonTokenStream);
			SharpMUSHParser.CommandContext chatContext = pennParser.command();
			SharpMUSHParserVisitor visitor = new(this);

			return visitor.Visit(chatContext);
		}
	}
}