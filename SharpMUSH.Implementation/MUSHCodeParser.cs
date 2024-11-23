using Antlr4.Runtime;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using System.Collections.Immutable;
using Mediator;
using Serilog;

namespace SharpMUSH.Implementation;

/// <summary>
/// Provides the parser.
/// Each call is Synchronous, and stateful at this time.
/// </summary>
public record MUSHCodeParser(
	IPasswordService _passwordService,
	IPermissionService _permissionService,
	IAttributeService _attributeService,
	INotifyService _notifyService,
	ILocateService _locateService,
	ICommandDiscoveryService _commandDiscoveryService,
	ITaskScheduler _scheduleService,
	IConnectionService _connectionService,
	IMediator _mediator) : IMUSHCodeParser
{
	public IPasswordService PasswordService => _passwordService;

	public IPermissionService PermissionService => _permissionService;

	public IAttributeService AttributeService => _attributeService;

	public ILocateService LocateService => _locateService;

	public ICommandDiscoveryService CommandDiscoveryService => _commandDiscoveryService;

	public ITaskScheduler Scheduler => _scheduleService;

	public INotifyService NotifyService => _notifyService;

	public IConnectionService ConnectionService => _connectionService;

	public IMediator Mediator => _mediator;

	public ParserState CurrentState => State.Peek();

	/// <summary>
	/// Stack may not be needed if we can bring ParserState into the custom Visitors.
	/// 
	/// Stack should be good enough, since we parse left-to-right when we consider the Visitors.
	/// However, we may run into issues when it comes to function-depth calculations.
	/// 
	/// Time to start drawing a tree to make sure we put things in the right spots.
	/// </summary>
	public IImmutableStack<ParserState> State { get; private init; } = ImmutableStack<ParserState>.Empty;
	
	public IMUSHCodeParser FromState(ParserState state) => new MUSHCodeParser(_passwordService, _permissionService,
		_attributeService, _notifyService, _locateService, _commandDiscoveryService, _scheduleService,
		_connectionService, _mediator, state);

	public IMUSHCodeParser Empty() => this with { State = ImmutableStack<ParserState>.Empty };

	public IMUSHCodeParser Push(ParserState state) => this with { State = State.Push(state) };

	public MUSHCodeParser(
		IPasswordService passwordService,
		IPermissionService permissionService,
		IAttributeService attributeService,
		INotifyService notifyService,
		ILocateService locateService,
		ICommandDiscoveryService commandDiscoveryService,
		ITaskScheduler scheduleService,
		IConnectionService connectionService,
		IMediator mediator,
		ParserState state) :
		this(passwordService, permissionService, attributeService, notifyService, locateService,
			commandDiscoveryService, scheduleService, connectionService, mediator)
		=> State = [state];

	public ValueTask<CallState?> FunctionParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(commonTokenStream)
		{
			Interpreter =
			{
				// sharpParser.Trace = true;
				// sharpParser.AddErrorListener(new DiagnosticErrorListener(false));
				// sharpParser.Interpreter.PredictionMode = Antlr4.Runtime.Atn.PredictionMode.SLL_EXACT_AMBIG_DETECTION;
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.SLL
			}
		};
		var chatContext = sharpParser.startPlainString();
		SharpMUSHParserVisitor visitor = new(this, text);

		return visitor.Visit(chatContext);
	}

	public ValueTask<CallState?> CommandListParse(MString text)
	{
		var plaintext = MModule.plainText(text);
		AntlrInputStreamSpan inputStream = new(plaintext);
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(commonTokenStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.SLL
			}
		};
		var chatContext = sharpParser.startCommandString();
		SharpMUSHParserVisitor visitor = new(this, text);

		return visitor.Visit(chatContext);
	}

	public Func<ValueTask<CallState?>> CommandListParseVisitor(MString text)
	{
		var plaintext = MModule.plainText(text);
		AntlrInputStreamSpan inputStream = new(plaintext);
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(commonTokenStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.SLL
			}
		};
		var chatContext = sharpParser.startCommandString();
		SharpMUSHParserVisitor visitor = new(this, text);

		return () => visitor.Visit(chatContext);
	}

	/// <summary>
	/// This is the main entry point for commands run by a player.
	/// </summary>
	/// <param name="handle">The handle that identifies the connection.</param>
	/// <param name="text">The text to parse.</param>
	/// <returns>A completed task.</returns>
	public async ValueTask CommandParse(string handle, MString text)
	{
		var handleId = ConnectionService.Get(handle);
		var newParser = Push(new ParserState(
			Registers: new([[]]),
			IterationRegisters: new(),
			RegexRegisters: new(),
			CurrentEvaluation: null,
			Function: null,
			Command: MModule.plainText(text),
			Switches: [],
			Arguments: [],
			Executor: handleId?.Ref,
			Enactor: handleId?.Ref,
			Caller: handleId?.Ref,
			Handle: handle));

		AntlrInputStreamSpan inputStream = new(MModule.plainText(text));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(commonTokenStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.SLL
			}
		};
		var chatContext = sharpParser.startSingleCommandString();
		SharpMUSHParserVisitor visitor = new(newParser, text);

		await visitor.Visit(chatContext);
	}

	/// <summary>
	/// This is the main entry point for commands run by a player.
	/// </summary>
	/// <param name="text">The text to parse.</param>
	/// <returns>A completed task.</returns>
	public async ValueTask CommandParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(commonTokenStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.SLL
			}
		};
		var chatContext = sharpParser.startSingleCommandString();
		SharpMUSHParserVisitor visitor = new(this, text);

		await visitor.Visit(chatContext);
	}

	public ValueTask<CallState?> CommandCommaArgsParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(commonTokenStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.SLL
			}
		};
		var chatContext = sharpParser.commaCommandArgs();
		SharpMUSHParserVisitor visitor = new(this, text);

		return visitor.Visit(chatContext);
	}

	public ValueTask<CallState?> CommandSingleArgParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(commonTokenStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.SLL
			}
		};
		var chatContext = sharpParser.startPlainSingleCommandArg();
		SharpMUSHParserVisitor visitor = new(this, text);

		return visitor.Visit(chatContext);
	}

	public ValueTask<CallState?> CommandEqSplitArgsParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(commonTokenStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.SLL
			}
		};
		var chatContext = sharpParser.startEqSplitCommandArgs();
		SharpMUSHParserVisitor visitor = new(this, text);

		return visitor.Visit(chatContext);
	}

	public ValueTask<CallState?> CommandEqSplitParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenStream commonTokenStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(commonTokenStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.SLL
			}
		};
		var chatContext = sharpParser.startEqSplitCommand();
		SharpMUSHParserVisitor visitor = new(this, text);

		return visitor.Visit(chatContext);
	}
}