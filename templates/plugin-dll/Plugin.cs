using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;

namespace PLUGIN_NAMESPACE;

/// <summary>
/// The single entry type for this plugin. Mark exactly one type per assembly with
/// <see cref="SharpPluginAttribute"/> and derive from <see cref="PluginBase"/>; the base's default
/// <c>GetCommands</c>/<c>GetFunctions</c> reflect the generator-produced
/// <c>SharpMUSH.Implementation.Generated.CommandLibrary.Commands</c> / <c>FunctionLibrary.Functions</c>
/// in THIS assembly, so authoring is identical to in-tree engine code: write ordinary
/// <see cref="SharpCommandAttribute"/>/<see cref="SharpFunctionAttribute"/> methods.
///
/// Override <c>Dependencies</c> (ids of plugins that must load first) and <c>Priority</c> (tie-break)
/// to influence load order; both have sensible defaults from <see cref="PluginBase"/>.
/// </summary>
[SharpPlugin]
public sealed class Plugin : PluginBase
{
	public override string Id => "PLUGIN_ID";
	public override string Version => "0.1.0";

	// ── A command. Register-tier is IsSystem=true (compiled C#, same as built-ins), so it enters the
	//    command trie and supports abbreviation. Resolve engine services AT CALL TIME from the
	//    parser's provider — the supported, unload-friendly pattern (never cache them in a field).
	[SharpCommand(Name = "+PLUGINCMDTOKEN", MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Hello(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var mediator = parser.ServiceProvider.GetRequiredService<IMediator>();
		var notify = parser.ServiceProvider.GetRequiredService<INotifyService>();

		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		await notify.Notify(executor, "Hello from the PLUGIN_ID plugin!", executor);

		return new CallState("Hello from the PLUGIN_ID plugin!");
	}

	// ── A function. Same authoring as in-tree code; reads its arguments off the parser state.
	[SharpFunction(Name = "PLUGINFNTOKEN", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Add(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var a = decimal.Parse(args["0"].Message!.ToPlainText());
		var b = decimal.Parse(args["1"].Message!.ToPlainText());
		return ValueTask.FromResult(
			new CallState((a + b).ToString(System.Globalization.CultureInfo.InvariantCulture)));
	}

	// ── Contributing beyond commands/functions (Phase 2a/2b) ──────────────────────────────────────
	// Implement any subset of the contribution/hook interfaces (all in SharpMUSH.Library.Plugins) to
	// extend the host further. Each is wired by the host's PluginCatalog — you register nothing.
	//
	//   IServiceRegistrar        → register your own DI services (pre-build, into the host container)
	//   IEndpointContributor     → map your own ASP.NET endpoints/SignalR hubs into the host pipeline
	//   IApplicationSource       → contribute portal UI: schema-driven Application(s) + NavBar entries
	//   IFlagSource              → seed engine object flags during DB migration
	//   IMigrationSource         → provider-tagged DB migrations (Arango/Memgraph/Surreal)
	//   IBridgeSubscriptionSource→ a NATS→SignalR background subscription
	//   ICommandInterceptor      → before/override/after a command (the C# analog of softcode @hook)
	//   IConnectionHook          → connect/login/disconnect
	//   IObjectLifecycleHook     → object created/destroying
	//
	// NOTE: a plugin that implements ONLY command/function/hook seams can be hot-unloaded at runtime.
	// Implementing any of IServiceRegistrar / IEndpointContributor / IApplicationSource / IFlagSource /
	// IMigrationSource / IBridgeSubscriptionSource captures load-once state, so such a plugin is load-once
	// (restart to reload). See docs/guides/writing-a-plugin.md for the full worked examples.
	//
	// Example — register one DI service:
	//
	// public void RegisterServices(IServiceCollection services)
	// 	=> services.AddSingleton<MyPluginService>();

	// ── Contributing a web surface (controllers + hubs/endpoints), ASP.NET-style ──────────────────
	//   1) ConfigureServices — from RegisterServices, call ordinary ASP.NET extensions. Expose MVC
	//      controllers defined in THIS assembly by adding it as an ApplicationPart (the "FromAssembly"
	//      load); add SignalR if you map a hub:
	//
	//      public void RegisterServices(IServiceCollection services)
	//      {
	//      	services.AddControllers().AddApplicationPart(typeof(Plugin).Assembly); // your [ApiController]s
	//      	services.AddSignalR();                                                 // needed if you map a hub
	//      }
	//
	//   2) Pipeline — implement IEndpointContributor to MAP hubs/routes (host calls this post-build,
	//      after mapping its own controllers/hubs):
	//
	//      public void MapEndpoints(IEndpointRouteBuilder endpoints)
	//      {
	//      	endpoints.MapHub<MyHub>("/hubs/mine");
	//      	endpoints.MapGet("/api/myplugin/ping", () => "pong");
	//      }
	//
	// Keep your DTOs/service interfaces INSIDE this plugin assembly. The host cannot compile-reference a
	// runtime-loaded plugin, so every host↔plugin and client↔plugin boundary must be a generic seam
	// (IServiceRegistrar / IEndpointContributor) or serialization (HTTP / SignalR JSON) — the client
	// defines its own DTO matching your wire shape. See SharpMUSH.Plugins.Scene (the reference plugin) and
	// docs/design/plugin-system.md for the canonical end-to-end example.
}
