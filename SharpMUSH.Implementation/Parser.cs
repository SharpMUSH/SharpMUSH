using Antlr4.Runtime;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using System.Collections.Immutable;

namespace SharpMUSH.Implementation
{
	/// <summary>
	/// Provides the parser.
	/// Each call is Synchronous, and stateful at this time.
	/// </summary>
	public class Parser(
			IPasswordService _passwordService,
			IPermissionService _permissionService,
			ISharpDatabase _database,
			INotifyService _notifyService,
			IQueueService _queueService,
			IConnectionService _connectionService)
	{
		public record ParserState(
			ImmutableDictionary<string, MString> Registers,
			DBAttribute? CurrentEvaluation,
			string? Function,
			string? Command,
			List<CallState> Arguments,
			DBRef Executor,
			DBRef Enactor,
			DBRef Caller);

		public IPasswordService PasswordService => _passwordService;
		
		public IPermissionService PermissionService => _permissionService;
		
		public ISharpDatabase Database => _database;

		public IQueueService QueueService => _queueService;

		public INotifyService NotifyService => _notifyService;

		public IConnectionService ConnectionService => _connectionService;

		public ParserState CurrentState => State.Peek();

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
		  INotifyService notifyService,
			IQueueService queueService,
			IConnectionService connectionService,
			ImmutableStack<ParserState> state) :
				this(passwordService, permissionService, database, notifyService, queueService, connectionService)
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
			INotifyService notifyService, 
			IQueueService queueService,
			IConnectionService connectionService,
			ParserState state) :
				this(passwordService, permissionService, database, notifyService, queueService, connectionService)
				=> State = [state];

		public CallState? FunctionParse(string text)
		{
			AntlrInputStream inputStream = new(text);
			SharpMUSHLexer sharpLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(sharpLexer);
			SharpMUSHParser sharpParser = new(commonTokenStream);
			SharpMUSHParser.PlainStringContext chatContext = sharpParser.plainString();
			SharpMUSHParserVisitor visitor = new(this);

			return visitor.Visit(chatContext);
		}

		public CallState? CommandListParse(string text)
		{
			AntlrInputStream inputStream = new(text);
			SharpMUSHLexer sharpLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(sharpLexer);
			SharpMUSHParser sharpParser = new(commonTokenStream);
			SharpMUSHParser.CommandListContext chatContext = sharpParser.commandList();
			SharpMUSHParserVisitor visitor = new(this);

			return visitor.Visit(chatContext);
		}

		// TODO: Executor should carry more than just dbref. Also port information.
		public CallState? CommandParse(string text)
		{
			AntlrInputStream inputStream = new(text);
			SharpMUSHLexer sharpLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(sharpLexer);
			SharpMUSHParser sharpParser = new(commonTokenStream);
			SharpMUSHParser.CommandContext chatContext = sharpParser.command();
			SharpMUSHParserVisitor visitor = new(this);

			return visitor.Visit(chatContext);
		}

		public CallState? CommandCommaArgsParse(string text)
		{
			AntlrInputStream inputStream = new(text);
			SharpMUSHLexer sharpLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(sharpLexer);
			SharpMUSHParser sharpParser = new(commonTokenStream);
			SharpMUSHParser.CommaCommandArgsContext chatContext = sharpParser.commaCommandArgs();
			SharpMUSHParserVisitor visitor = new(this);

			return visitor.Visit(chatContext);
		}

		public CallState? CommandSingleArgParse(string text)
		{
			AntlrInputStream inputStream = new(text);
			SharpMUSHLexer sharpLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(sharpLexer);
			SharpMUSHParser sharpParser = new(commonTokenStream);
			SharpMUSHParser.SingleCommandArgContext chatContext = sharpParser.singleCommandArg();
			SharpMUSHParserVisitor visitor = new(this);

			return visitor.Visit(chatContext);
		}

		public CallState? CommandEqSplitArgsParse(string text)
		{
			AntlrInputStream inputStream = new(text);
			SharpMUSHLexer sharpLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(sharpLexer);
			SharpMUSHParser sharpParser = new(commonTokenStream);
			SharpMUSHParser.EqsplitCommandArgsContext chatContext = sharpParser.eqsplitCommandArgs();
			SharpMUSHParserVisitor visitor = new(this);

			return visitor.Visit(chatContext);
		}

		public CallState? CommandEqSplitParse(string text)
		{
			AntlrInputStream inputStream = new(text);
			SharpMUSHLexer sharpLexer = new(inputStream);
			CommonTokenStream commonTokenStream = new(sharpLexer);
			SharpMUSHParser sharpParser = new(commonTokenStream);
			SharpMUSHParser.EqsplitCommandContext chatContext = sharpParser.eqsplitCommand();
			SharpMUSHParserVisitor visitor = new(this);

			return visitor.Visit(chatContext);
		}
	}
}