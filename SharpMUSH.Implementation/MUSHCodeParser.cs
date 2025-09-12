using Antlr4.Runtime;
using Mediator;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneOf.Types;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation;

/// <summary>
/// Provides the parser.
/// Each call is Synchronous and Stateful at this time.
/// </summary>
public record MUSHCodeParser(ILogger<MUSHCodeParser> Logger,
	LibraryService<string, Library.Definitions.FunctionDefinition> FunctionLibrary,
	LibraryService<string, Library.Definitions.CommandDefinition> CommandLibrary,
	IServiceProvider ServiceProvider) : IMUSHCodeParser
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

	public IMUSHCodeParser FromState(ParserState state) => new MUSHCodeParser(Logger, FunctionLibrary, CommandLibrary, ServiceProvider, state);
	
	public Option<ParserState> StateHistory(uint index)
	{
		try
		{
			var a = State.Take((int)index);
			return a.Last();
		}
		catch
		{
			return new None();
		}
	}

	public IMUSHCodeParser Empty() => this with { State = ImmutableStack<ParserState>.Empty };

	public IMUSHCodeParser Push(ParserState state) => this with { State = State.Push(state) };

	public MUSHCodeParser(ILogger<MUSHCodeParser> logger, 
		LibraryService<string, Library.Definitions.FunctionDefinition> functionLibrary,
		LibraryService<string, Library.Definitions.CommandDefinition> commandLibrary,
		IServiceProvider serviceProvider,
		ParserState state) : this(logger, functionLibrary, commandLibrary, serviceProvider)
		=> State = [state];

	public ValueTask<CallState?> FunctionParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text).AsMemory(), nameof(FunctionParse));
		SharpMUSHLexer sharpLexer = new(inputStream);
		BufferedTokenSpanStream bufferedTokenSpanStream = new(sharpLexer);
		bufferedTokenSpanStream.Fill();
		SharpMUSHParser sharpParser = new(bufferedTokenSpanStream)
		{
			Interpreter =
			{
				// sharpParser.Trace = true;
				// sharpParser.AddErrorListener(new DiagnosticErrorListener(false));
				// sharpParser.Interpreter.PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL;
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			},
			Trace = false
		};
		// sharpParser.AddErrorListener(new DiagnosticErrorListener(false));

		var chatContext = sharpParser.startPlainString();
		SharpMUSHParserVisitor visitor = new(Logger, this, 
			ServiceProvider.GetRequiredService<IOptionsMonitor<PennMUSHOptions>>(),
			ServiceProvider.GetRequiredService<IMediator>(),
			ServiceProvider.GetRequiredService<INotifyService>(),
			ServiceProvider.GetRequiredService<IConnectionService>(),
			ServiceProvider.GetRequiredService<ILocateService>(),
			ServiceProvider.GetRequiredService<ICommandDiscoveryService>(),
			ServiceProvider.GetRequiredService<IAttributeService>(),
			text);

		return visitor.Visit(chatContext);
	}

	public ValueTask<CallState?> CommandListParse(MString text)
	{
		var plaintext = MModule.plainText(text);
		AntlrInputStreamSpan inputStream = new(plaintext.AsMemory(), nameof(CommandListParse));
		SharpMUSHLexer sharpLexer = new(inputStream);
		BufferedTokenSpanStream bufferedTokenSpanStream = new(sharpLexer);
		bufferedTokenSpanStream.Fill();
		SharpMUSHParser sharpParser = new(bufferedTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			}
		};
		var chatContext = sharpParser.startCommandString();
		SharpMUSHParserVisitor visitor = new(Logger, this,
			ServiceProvider.GetRequiredService<IOptionsMonitor<PennMUSHOptions>>(),
			ServiceProvider.GetRequiredService<IMediator>(),
			ServiceProvider.GetRequiredService<INotifyService>(),
			ServiceProvider.GetRequiredService<IConnectionService>(),
			ServiceProvider.GetRequiredService<ILocateService>(),
			ServiceProvider.GetRequiredService<ICommandDiscoveryService>(),
			ServiceProvider.GetRequiredService<IAttributeService>(), text);

		return visitor.Visit(chatContext);
	}

	public Func<ValueTask<CallState?>> CommandListParseVisitor(MString text)
	{
		var plaintext = MModule.plainText(text);
		AntlrInputStreamSpan inputStream = new(plaintext.AsMemory(), nameof(CommandListParseVisitor));
		SharpMUSHLexer sharpLexer = new(inputStream);
		BufferedTokenSpanStream bufferedTokenSpanStream = new(sharpLexer);
		bufferedTokenSpanStream.Fill();
		SharpMUSHParser sharpParser = new(bufferedTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			}
		};
		var chatContext = sharpParser.startCommandString();
		
		SharpMUSHParserVisitor visitor = new(Logger, this, 
			ServiceProvider.GetRequiredService<IOptionsMonitor<PennMUSHOptions>>(),
			ServiceProvider.GetRequiredService<IMediator>(),
			ServiceProvider.GetRequiredService<INotifyService>(),
			ServiceProvider.GetRequiredService<IConnectionService>(),
			ServiceProvider.GetRequiredService<ILocateService>(),
			ServiceProvider.GetRequiredService<ICommandDiscoveryService>(),
			ServiceProvider.GetRequiredService<IAttributeService>(),text);

		return () => visitor.Visit(chatContext);
	}

	/// <summary>
	/// This is the main entry point for commands run by a player.
	/// </summary>
	/// <param name="handle">The handle that identifies the connection.</param>
	/// <param name="text">The text to parse.</param>
	/// <returns>A completed task.</returns>
	public async ValueTask CommandParse(long handle, IConnectionService connectionService, MString text)
	{
		var handleId = connectionService.Get(handle);
		var newParser = Push(new ParserState(
			Registers: new([[]]),
			IterationRegisters: [],
			RegexRegisters: [],
			CurrentEvaluation: null,
			ExecutionStack: [],
			ParserFunctionDepth: 0,
			Function: null,
			Command: MModule.plainText(text),
			CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
			Switches: [],
			Arguments: [],
			Executor: handleId?.Ref,
			Enactor: handleId?.Ref,
			Caller: handleId?.Ref,
			Handle: handle));

		AntlrInputStreamSpan inputStream = new(MModule.plainText(text).AsMemory(), nameof(CommandParse));
		SharpMUSHLexer sharpLexer = new(inputStream);
		BufferedTokenSpanStream bufferedTokenSpanStream = new(sharpLexer);
		bufferedTokenSpanStream.Fill();
		SharpMUSHParser sharpParser = new(bufferedTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			}
		};
		var chatContext = sharpParser.startSingleCommandString();
		SharpMUSHParserVisitor visitor = new(Logger, newParser,
			ServiceProvider.GetRequiredService<IOptionsMonitor<PennMUSHOptions>>(),
			ServiceProvider.GetRequiredService<IMediator>(),
			ServiceProvider.GetRequiredService<INotifyService>(),
			ServiceProvider.GetRequiredService<IConnectionService>(),
			ServiceProvider.GetRequiredService<ILocateService>(),
			ServiceProvider.GetRequiredService<ICommandDiscoveryService>(),
			ServiceProvider.GetRequiredService<IAttributeService>(), text);

		await visitor.Visit(chatContext);
	}

	/// <summary>
	/// This is the main entry point for commands run by a player.
	/// </summary>
	/// <param name="text">The text to parse.</param>
	/// <returns>A completed task.</returns>
	public async ValueTask CommandParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text).AsMemory(), nameof(CommandParse));
		SharpMUSHLexer sharpLexer = new(inputStream);
		BufferedTokenSpanStream bufferedTokenSpanStream = new(sharpLexer);
		bufferedTokenSpanStream.Fill();
		SharpMUSHParser sharpParser = new(bufferedTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			}
		};
		var chatContext = sharpParser.startSingleCommandString();
		SharpMUSHParserVisitor visitor = new(Logger, this,
			ServiceProvider.GetRequiredService<IOptionsMonitor<PennMUSHOptions>>(),
			ServiceProvider.GetRequiredService<IMediator>(),
			ServiceProvider.GetRequiredService<INotifyService>(),
			ServiceProvider.GetRequiredService<IConnectionService>(),
			ServiceProvider.GetRequiredService<ILocateService>(),
			ServiceProvider.GetRequiredService<ICommandDiscoveryService>(),
			ServiceProvider.GetRequiredService<IAttributeService>(), text);

		await visitor.Visit(chatContext);
	}

	public ValueTask<CallState?> CommandCommaArgsParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text).AsMemory(), nameof(CommandCommaArgsParse));
		SharpMUSHLexer sharpLexer = new(inputStream);
		BufferedTokenSpanStream bufferedTokenSpanStream = new(sharpLexer);
		bufferedTokenSpanStream.Fill();
		SharpMUSHParser sharpParser = new(bufferedTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			}
		};
		var chatContext = sharpParser.commaCommandArgs();
		SharpMUSHParserVisitor visitor = new(Logger, this,
			ServiceProvider.GetRequiredService<IOptionsMonitor<PennMUSHOptions>>(),
			ServiceProvider.GetRequiredService<IMediator>(),
			ServiceProvider.GetRequiredService<INotifyService>(),
			ServiceProvider.GetRequiredService<IConnectionService>(),
			ServiceProvider.GetRequiredService<ILocateService>(),
			ServiceProvider.GetRequiredService<ICommandDiscoveryService>(),
			ServiceProvider.GetRequiredService<IAttributeService>(), text);

		return visitor.Visit(chatContext);
	}

	public ValueTask<CallState?> CommandSingleArgParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text).AsMemory(), nameof(CommandSingleArgParse));
		SharpMUSHLexer sharpLexer = new(inputStream);
		BufferedTokenSpanStream bufferedTokenSpanStream = new(sharpLexer);
		bufferedTokenSpanStream.Fill();
		SharpMUSHParser sharpParser = new(bufferedTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			}
		};
		var chatContext = sharpParser.startPlainSingleCommandArg();
		SharpMUSHParserVisitor visitor = new(Logger, this,
			ServiceProvider.GetRequiredService<IOptionsMonitor<PennMUSHOptions>>(),
			ServiceProvider.GetRequiredService<IMediator>(),
			ServiceProvider.GetRequiredService<INotifyService>(),
			ServiceProvider.GetRequiredService<IConnectionService>(),
			ServiceProvider.GetRequiredService<ILocateService>(),
			ServiceProvider.GetRequiredService<ICommandDiscoveryService>(),
			ServiceProvider.GetRequiredService<IAttributeService>(), text);

		return visitor.Visit(chatContext);
	}

	public ValueTask<CallState?> CommandEqSplitArgsParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text).AsMemory(), nameof(CommandEqSplitArgsParse));
		SharpMUSHLexer sharpLexer = new(inputStream);
		BufferedTokenSpanStream bufferedTokenSpanStream = new(sharpLexer);
		bufferedTokenSpanStream.Fill();
		SharpMUSHParser sharpParser = new(bufferedTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			}
		};

		var chatContext = sharpParser.startEqSplitCommandArgs();
		SharpMUSHParserVisitor visitor = new(Logger, this,
			ServiceProvider.GetRequiredService<IOptionsMonitor<PennMUSHOptions>>(),
			ServiceProvider.GetRequiredService<IMediator>(),
			ServiceProvider.GetRequiredService<INotifyService>(),
			ServiceProvider.GetRequiredService<IConnectionService>(),
			ServiceProvider.GetRequiredService<ILocateService>(),
			ServiceProvider.GetRequiredService<ICommandDiscoveryService>(),
			ServiceProvider.GetRequiredService<IAttributeService>(), text);

		return visitor.Visit(chatContext);
	}

	public ValueTask<CallState?> CommandEqSplitParse(MString text)
	{
		AntlrInputStreamSpan inputStream = new(MModule.plainText(text).AsMemory(), nameof(CommandEqSplitParse));
		SharpMUSHLexer sharpLexer = new(inputStream);
		BufferedTokenSpanStream bufferedTokenSpanStream = new(sharpLexer);
		bufferedTokenSpanStream.Fill();
		SharpMUSHParser sharpParser = new(bufferedTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = Antlr4.Runtime.Atn.PredictionMode.LL
			}
		};
		var chatContext = sharpParser.startEqSplitCommand();
		SharpMUSHParserVisitor visitor = new(Logger, this,
			ServiceProvider.GetRequiredService<IOptionsMonitor<PennMUSHOptions>>(),
			ServiceProvider.GetRequiredService<IMediator>(),
			ServiceProvider.GetRequiredService<INotifyService>(),
			ServiceProvider.GetRequiredService<IConnectionService>(),
			ServiceProvider.GetRequiredService<ILocateService>(),
			ServiceProvider.GetRequiredService<ICommandDiscoveryService>(),
			ServiceProvider.GetRequiredService<IAttributeService>(), text);

		return visitor.Visit(chatContext);
	}
}