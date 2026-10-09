using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Commands;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// The services evaluation uses, resolved once per parser rather than per call, and the pipelines
/// built over them. A <see cref="MUSHCodeParser"/> builds one when it is constructed and every
/// state it pushes, and every visitor it creates, shares it.
/// </summary>
/// <remarks>
/// <para>The required services are resolved eagerly, as the parser always did. The optional ones —
/// telemetry, the <c>@function</c> registry, plugin command interceptors and the reality policy —
/// used to be located on every function call or command; they are resolved on first use and kept,
/// which is the same instance every time because each is a singleton.</para>
/// <para>Nothing here is per evaluation: the per-parse state (source text, debug nesting, brace
/// depth) lives on the visitor, and options and logger are read from the parser, since a parser
/// copied with other options must evaluate under them.</para>
/// </remarks>
internal sealed class EvaluationServices
{
	private ITelemetryService? _telemetry;
	private IUserDefinedFunctionService? _userFunctions;
	private IPluginHookDispatcher? _pluginHooks;
	private IRealityPolicy? _reality;
	private IPipeOutputCapture? _pipeCapture;
	private ExecutorDebugFlags? _debugFlags;

	/// <param name="provider">Where the optional services are located.</param>
	/// <param name="locateOptional">
	/// Whether the optional services are located at all. A visitor constructed directly over a parser
	/// whose <see cref="IMUSHCodeParser.LocatesOptionalServices"/> is false never had them.
	/// </param>
	public EvaluationServices(
		IServiceProvider provider,
		IMediator mediator,
		INotifyService notifyService,
		IConnectionService connectionService,
		ILocateService locateService,
		ICommandDiscoveryService commandDiscoveryService,
		IAttributeService attributeService,
		IHookService hookService,
		ILockService lockService,
		bool locateOptional = true)
	{
		Provider = provider;
		LocatesOptional = locateOptional;
		Mediator = mediator;
		NotifyService = notifyService;
		ConnectionService = connectionService;
		LocateService = locateService;
		CommandDiscoveryService = commandDiscoveryService;
		AttributeService = attributeService;
		HookService = hookService;
		LockService = lockService;
		Diagnostics = new EvaluationDiagnostics(this);
		Functions = new FunctionInvocationPipeline(this);
		Arguments = new CommandArgumentSplitter(this);
		Commands = new CommandInvocationPipeline(this);
		StandardAttributes = new StandardAttributeCommand(this);
		Dispatcher = new CommandDispatcher(this);
	}

	/// <summary>Resolves the required services from <paramref name="provider"/>.</summary>
	public static EvaluationServices From(IServiceProvider provider) => new(provider,
		provider.GetRequiredService<IMediator>(),
		provider.GetRequiredService<INotifyService>(),
		provider.GetRequiredService<IConnectionService>(),
		provider.GetRequiredService<ILocateService>(),
		provider.GetRequiredService<ICommandDiscoveryService>(),
		provider.GetRequiredService<IAttributeService>(),
		provider.GetRequiredService<IHookService>(),
		provider.GetRequiredService<ILockService>());

	/// <summary>
	/// These services, locating the optional ones in <paramref name="provider"/> instead. The required
	/// services stay the ones this parser was built with, as they always did when a parser was copied
	/// with another provider; the optional ones were always located in the parser's current provider.
	/// </summary>
	public EvaluationServices For(IServiceProvider provider) => ReferenceEquals(provider, Provider)
		? this
		: new EvaluationServices(provider, Mediator, NotifyService, ConnectionService, LocateService,
			CommandDiscoveryService, AttributeService, HookService, LockService, LocatesOptional);

	public IServiceProvider Provider { get; }
	private bool LocatesOptional { get; }

	public IMediator Mediator { get; }
	public INotifyService NotifyService { get; }
	public IConnectionService ConnectionService { get; }
	public ILocateService LocateService { get; }
	public ICommandDiscoveryService CommandDiscoveryService { get; }
	public IAttributeService AttributeService { get; }
	public IHookService HookService { get; }
	public ILockService LockService { get; }

	/// <summary>Records function and command timings; absent in hosts that do not register it.</summary>
	public ITelemetryService? Telemetry
		=> _telemetry ??= LocatesOptional ? Provider.GetService<ITelemetryService>() : null;

	/// <summary>The <c>@function</c> registry and built-in restrictions; absent in hosts that do not register it.</summary>
	public IUserDefinedFunctionService? UserFunctions
		=> _userFunctions ??= LocatesOptional ? Provider.GetService<IUserDefinedFunctionService>() : null;

	/// <summary>Plugin command interceptors; absent in hosts that do not register them.</summary>
	public IPluginHookDispatcher? PluginHooks
		=> _pluginHooks ??= LocatesOptional ? Provider.GetService<IPluginHookDispatcher>() : null;

	/// <summary>
	/// The reality policy <c>$</c>-command discovery perceives through. Required: a host without one
	/// fails the first <c>$</c>-command lookup, as it always did.
	/// </summary>
	public IRealityPolicy Reality => _reality ??= Provider.GetRequiredService<IRealityPolicy>();

	/// <summary>
	/// The executor's DEBUG flag, kept between writes; absent in hosts that do not register it, where the
	/// flag is read on each call.
	/// </summary>
	public ExecutorDebugFlags? DebugFlags
		=> _debugFlags ??= LocatesOptional ? Provider.GetService<ExecutorDebugFlags>() : null;

	/// <summary>Takes a piped command's output for the next command's <c>%|</c>; absent in hosts that do not register it.</summary>
	public IPipeOutputCapture? PipeCapture
		=> _pipeCapture ??= LocatesOptional ? Provider.GetService<IPipeOutputCapture>() : null;

	public EvaluationDiagnostics Diagnostics { get; }
	public FunctionInvocationPipeline Functions { get; }
	public CommandArgumentSplitter Arguments { get; }
	public CommandInvocationPipeline Commands { get; }
	public StandardAttributeCommand StandardAttributes { get; }
	public CommandDispatcher Dispatcher { get; }
}
