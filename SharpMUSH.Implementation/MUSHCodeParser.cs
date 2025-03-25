using Antlr4.Runtime;
using Mediator;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Implementation;

/// <summary>
/// Provides the parser.
/// Each call is Synchronous, and stateful at this time.
/// </summary>
public record MUSHCodeParser(
	ILogger<MUSHCodeParser> Logger,
	IOptionsMonitor<PennMUSHOptions> Configuration,
	IPasswordService PasswordService,
	IPermissionService PermissionService,
	IAttributeService AttributeService,
	INotifyService NotifyService,
	ILocateService LocateService,
	IExpandedObjectDataService ObjectDataService,
	ICommandDiscoveryService CommandDiscoveryService,
	ITaskScheduler Scheduler,
	IConnectionService ConnectionService,
	IMediator Mediator) : IMUSHCodeParser
{
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

	public IMUSHCodeParser FromState(ParserState state) => new MUSHCodeParser(Logger, Configuration, PasswordService,
		PermissionService,
		AttributeService, NotifyService, LocateService, ObjectDataService, CommandDiscoveryService, Scheduler,
		ConnectionService, Mediator, state);

	public IMUSHCodeParser Empty() => this with { State = ImmutableStack<ParserState>.Empty };

	public IMUSHCodeParser Push(ParserState state) => this with { State = State.Push(state) };

	public MUSHCodeParser(
		ILogger<MUSHCodeParser> logger,
		IOptionsMonitor<PennMUSHOptions> config,
		IPasswordService passwordService,
		IPermissionService permissionService,
		IAttributeService attributeService,
		INotifyService notifyService,
		ILocateService locateService,
		IExpandedObjectDataService objectDataService,
		ICommandDiscoveryService commandDiscoveryService,
		ITaskScheduler scheduleService,
		IConnectionService connectionService,
		IMediator mediator,
		ParserState state) :
		this(logger, config, passwordService, permissionService, attributeService, notifyService, locateService,
			objectDataService, commandDiscoveryService, scheduleService, connectionService, mediator)
		=> State = [state];

	public ValueTask<CallState?> FunctionParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text), nameof(FunctionParse));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenSpanStream CommonTokenSpanStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(CommonTokenSpanStream)
		{
			Interpreter =
			{
				// sharpParser.Trace = true;
				// sharpParser.AddErrorListener(new DiagnosticErrorListener(false));
				// sharpParser.Interpreter.PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL_EXACT_AMBIG_DETECTION;
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			}
		};

		var chatContext = sharpParser.startPlainString();
		SharpMUSHParserVisitor visitor = new(Logger, this, text);

		return visitor.Visit(chatContext);
	}

	public ValueTask<CallState?> CommandListParse(MString text)
	{
		var plaintext = MModule.plainText(text);
		AntlrInputStreamSpan inputStream = new(plaintext, nameof(CommandListParse));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenSpanStream CommonTokenSpanStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(CommonTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			}
		};
		var chatContext = sharpParser.startCommandString();
		SharpMUSHParserVisitor visitor = new(Logger, this, text);

		return visitor.Visit(chatContext);
	}

	public Func<ValueTask<CallState?>> CommandListParseVisitor(MString text)
	{
		var plaintext = MModule.plainText(text);
		AntlrInputStreamSpan inputStream = new(plaintext, nameof(CommandListParseVisitor));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenSpanStream CommonTokenSpanStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(CommonTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			}
		};
		var chatContext = sharpParser.startCommandString();
		SharpMUSHParserVisitor visitor = new(Logger, this, text);

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
			0,
			Function: null,
			Command: MModule.plainText(text),
			Switches: [],
			Arguments: [],
			Executor: handleId?.Ref,
			Enactor: handleId?.Ref,
			Caller: handleId?.Ref,
			Handle: handle));

		AntlrInputStreamSpan inputStream = new(MModule.plainText(text), nameof(CommandParse));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenSpanStream CommonTokenSpanStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(CommonTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			}
		};
		var chatContext = sharpParser.startSingleCommandString();
		SharpMUSHParserVisitor visitor = new(Logger, newParser, text);

		await visitor.Visit(chatContext);
	}

	/// <summary>
	/// This is the main entry point for commands run by a player.
	/// </summary>
	/// <param name="text">The text to parse.</param>
	/// <returns>A completed task.</returns>
	public async ValueTask CommandParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text), nameof(CommandParse));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenSpanStream CommonTokenSpanStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(CommonTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			}
		};
		var chatContext = sharpParser.startSingleCommandString();
		SharpMUSHParserVisitor visitor = new(Logger, this, text);

		await visitor.Visit(chatContext);
	}

	public ValueTask<CallState?> CommandCommaArgsParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text), nameof(CommandCommaArgsParse));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenSpanStream CommonTokenSpanStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(CommonTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			}
		};
		var chatContext = sharpParser.commaCommandArgs();
		SharpMUSHParserVisitor visitor = new(Logger, this, text);

		return visitor.Visit(chatContext);
	}

	public ValueTask<CallState?> CommandSingleArgParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text), nameof(CommandSingleArgParse));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenSpanStream CommonTokenSpanStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(CommonTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			}
		};
		var chatContext = sharpParser.startPlainSingleCommandArg();
		SharpMUSHParserVisitor visitor = new(Logger, this, text);

		return visitor.Visit(chatContext);
	}

	public ValueTask<CallState?> CommandEqSplitArgsParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text), nameof(CommandEqSplitArgsParse));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenSpanStream CommonTokenSpanStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(CommonTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			}
		};

		sharpParser.Trace = true;
		sharpParser.AddErrorListener(new DiagnosticErrorListener(false));

		var chatContext = sharpParser.startEqSplitCommandArgs();
		SharpMUSHParserVisitor visitor = new(Logger, this, text);

		return visitor.Visit(chatContext);
	}

	public ValueTask<CallState?> CommandEqSplitParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text), nameof(CommandEqSplitParse));
		SharpMUSHLexer sharpLexer = new(inputStream);
		CommonTokenSpanStream CommonTokenSpanStream = new(sharpLexer);
		SharpMUSHParser sharpParser = new(CommonTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			}
		};
		var chatContext = sharpParser.startEqSplitCommand();
		SharpMUSHParserVisitor visitor = new(Logger, this, text);

		return visitor.Visit(chatContext);
	}
}