using Antlr4.Runtime;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using System.Collections.Immutable;

namespace SharpMUSH.Implementation;

/// <summary>
/// Provides the parser.
/// Each call is Synchronous, and stateful at this time.
/// </summary>
public class MUSHCodeParser(
	IPasswordService _passwordService,
	IPermissionService _permissionService,
	ISharpDatabase _database,
	INotifyService _notifyService,
	IQueueService _queueService,
	IConnectionService _connectionService) : IMUSHCodeParser
{
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
	public IImmutableStack<ParserState> State { get; private set; } = ImmutableStack<ParserState>.Empty;

	public MUSHCodeParser(
		IPasswordService passwordService,
		IPermissionService permissionService,
		ISharpDatabase database,
		INotifyService notifyService,
		IQueueService queueService,
		IConnectionService connectionService,
		ImmutableStack<ParserState> state) :
		this(passwordService, permissionService, database, notifyService, queueService, connectionService)
		=> State = state;

	// Add register state. Which is also a Dictionary. The functions that recover etc a register state, are responsible themselves.

	public IMUSHCodeParser Push(ParserState state)
	{
		State = State.Push(state);
		return this;
	}

	public IMUSHCodeParser Pop()
	{
		State = State.Pop();
		return this;
	}

	public MUSHCodeParser(
		IPasswordService passwordService,
		IPermissionService permissionService,
		ISharpDatabase database,
		INotifyService notifyService,
		IQueueService queueService,
		IConnectionService connectionService,
		ParserState state) :
		this(passwordService, permissionService, database, notifyService, queueService, connectionService)
		=> State = [state];

	public CallState? FunctionParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(commonTokenStream);
		sharpParser.AddErrorListener(new DiagnosticErrorListener());
		sharpParser.Interpreter.PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL_EXACT_AMBIG_DETECTION;
		SharpMUSHParser.StartPlainStringContext chatContext = sharpParser.startPlainString();
		SharpMUSHParserVisitor visitor = new(this,text);

		return visitor.Visit(chatContext);
	}

	public CallState? CommandListParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(commonTokenStream);
		SharpMUSHParser.StartCommandStringContext chatContext = sharpParser.startCommandString();
		SharpMUSHParserVisitor visitor = new(this,text);

		return visitor.Visit(chatContext);
	}

	/// <summary>
	/// This is the main entry point for commands run by a player.
	/// </summary>
	/// <param name="handle">The handle that identifies the connection.</param>
	/// <param name="text">The text to parse.</param>
	/// <returns>A completed task.</returns>
	public Task CommandParse(string handle, MString text)
	{
		var handleId = ConnectionService.Get(handle);
		State = State.Push(new ParserState(
			new ([[]]),
			null,
			null,
			MModule.plainText(text),
			[],
			handleId?.Ref,
			handleId?.Ref,
			handleId?.Ref,
			handle));

		AntlrInputStreamSpan inputStream = new(MModule.plainText(text));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(commonTokenStream);
		SharpMUSHParser.StartSingleCommandStringContext chatContext = sharpParser.startSingleCommandString();
		SharpMUSHParserVisitor visitor = new(this,text);

		visitor.Visit(chatContext);
		return Task.CompletedTask;
	}

	public CallState? CommandCommaArgsParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(commonTokenStream);
		SharpMUSHParser.CommaCommandArgsContext chatContext = sharpParser.commaCommandArgs();
		SharpMUSHParserVisitor visitor = new(this,text);

		return visitor.Visit(chatContext);
	}

	public CallState? CommandSingleArgParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(commonTokenStream);
		SharpMUSHParser.StartPlainSingleCommandArgContext chatContext = sharpParser.startPlainSingleCommandArg();
		SharpMUSHParserVisitor visitor = new(this,text);

		return visitor.Visit(chatContext);
	}

	public CallState? CommandEqSplitArgsParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(commonTokenStream);
		SharpMUSHParser.StartEqSplitCommandArgsContext chatContext = sharpParser.startEqSplitCommandArgs();
		SharpMUSHParserVisitor visitor = new(this,text);

		return visitor.Visit(chatContext);
	}

	public CallState? CommandEqSplitParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(commonTokenStream);
		SharpMUSHParser.StartEqSplitCommandContext chatContext = sharpParser.startEqSplitCommand();
		SharpMUSHParserVisitor visitor = new(this,text);

		return visitor.Visit(chatContext);
	}
}