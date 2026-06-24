using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using Mediator;

namespace SharpMUSH.Tests.Integration.Integration;

/// <summary>
/// End-to-end proof of the Phase 1 C# plugin loader. The SamplePlugin fixture DLL (+ plugin.json) is
/// copied into the test host's <c>plugins/sample/</c> directory at build time; the booting server's
/// <c>PluginBootstrapService</c> discovers, loads, and registers its <c>[SharpCommand]</c>/<c>[SharpFunction]</c>
/// with IsSystem=true. These tests confirm the plugin command dispatches (including via abbreviation,
/// which only works because the entry lands in the IsSystem command trie) and the function evaluates.
/// </summary>
[NotInParallel]
public class PluginLoaderIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private IMUSHCodeParser CommandParser => WebAppFactory.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactory.FunctionParser;
	private IConnectionService Connection => WebAppFactory.Services.GetRequiredService<IConnectionService>();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText();

	private async Task<string?> Cmd(string command) =>
		(await CommandParser.CommandParse(1, Connection, MModule.single(command))).Message?.ToPlainText();

	[Test]
	public async Task PluginFunction_PluginAdd_EvaluatesToSum()
	{
		var result = await Eval("pluginadd(2,3)");
		await Assert.That(result).IsEqualTo("5");
	}

	[Test]
	public async Task PluginCommand_Ping_Dispatches()
	{
		var result = await Cmd("+ping");
		await Assert.That(result).IsEqualTo("Pong from the sample plugin!");
	}

	[Test]
	public async Task PluginCommand_Ping_DispatchesViaAbbreviation()
	{
		// Abbreviated command resolution only succeeds because the plugin entry was registered with
		// IsSystem=true and thus added to the prefix command trie alongside the built-ins.
		var result = await Cmd("+pi");
		await Assert.That(result).IsEqualTo("Pong from the sample plugin!");
	}

	[Test]
	public async Task ServiceRegistrar_PluginService_IsResolvablePostBoot()
	{
		// The SamplePlugin's IServiceRegistrar ran pre-build (during ConfigureServices) and added its
		// SamplePluginService to the host container. Resolve it by name across the plugin isolation boundary.
		var serviceType = FindPluginType("SamplePlugin.SamplePluginService");
		await Assert.That(serviceType).IsNotNull();

		var service = WebAppFactory.Services.GetService(serviceType!);
		await Assert.That(service).IsNotNull();

		var marker = serviceType!.GetProperty("Marker")!.GetValue(service) as string;
		await Assert.That(marker).IsEqualTo("sample-plugin-service");
	}

	[Test]
	public async Task FlagSource_PluginFlag_IsSeeded()
	{
		// The SamplePlugin's IFlagSource flag rode the same migration plumbing as the built-in flags and
		// must exist in the active database after migration.
		var mediator = WebAppFactory.Services.GetRequiredService<IMediator>();
		var flag = await mediator.Send(new GetObjectFlagQuery("SAMPLE_PLUGIN"));

		await Assert.That(flag).IsNotNull();
		await Assert.That(flag!.Name).IsEqualTo("SAMPLE_PLUGIN");
		await Assert.That(flag.Symbol).IsEqualTo("p");
	}

	[Test]
	public async Task BridgeSubscriptionSource_IsRunByBridgeService()
	{
		// NatsBridgeService runs every cataloged IBridgeSubscriptionSource alongside its built-ins. The
		// SamplePlugin's source sets a static flag on first invocation; poll across the isolation boundary
		// via reflection (the bridge runs asynchronously, so allow it a moment to start).
		var catalog = WebAppFactory.Services.GetRequiredService<PluginCatalog>();
		await Assert.That(catalog.BridgeSources).IsNotEmpty();

		var sourceType = catalog.BridgeSources[0].GetType();
		var ranField = sourceType.DeclaringType?.GetField("BridgeSubscriptionRan",
			               BindingFlags.Public | BindingFlags.Static)
		               ?? sourceType.GetField("BridgeSubscriptionRan", BindingFlags.Public | BindingFlags.Static);
		await Assert.That(ranField).IsNotNull();

		var ran = false;
		for (var i = 0; i < 50 && !ran; i++)
		{
			ran = (bool)ranField!.GetValue(null)!;
			if (!ran) await Task.Delay(100);
		}

		await Assert.That(ran).IsTrue();
	}

	[Test]
	public async Task CommandInterceptor_ObservesDispatchedCommand()
	{
		// The SamplePlugin's ICommandInterceptor is cataloged and consulted at the command seam. Dispatching
		// any command must record it in BeforeAsync (LastCommandObserved) and increment AfterAsync's counter.
		var pluginType = FindPluginType("SamplePlugin.SamplePlugin")!;
		var lastObservedField = pluginType.GetField("LastCommandObserved", BindingFlags.Public | BindingFlags.Static)!;
		var afterCountField = pluginType.GetField("AfterCommandCount", BindingFlags.Public | BindingFlags.Static)!;

		var beforeCount = (int)afterCountField.GetValue(null)!;
		await Cmd("think interceptor-probe");

		await Assert.That((string?)lastObservedField.GetValue(null)).IsNotNull();
		await Assert.That(((string?)lastObservedField.GetValue(null))!.Contains("interceptor-probe")).IsTrue();
		await Assert.That((int)afterCountField.GetValue(null)!).IsGreaterThan(beforeCount);
	}

	[Test]
	public async Task CommandInterceptor_VetoesCommand_BodyDoesNotRun()
	{
		// The interceptor vetoes "+vetome" by returning false from BeforeAsync; the engine must skip the
		// command body, so the plugin command's VetoCommandBodyRan flag stays false.
		var pluginType = FindPluginType("SamplePlugin.SamplePlugin")!;
		var bodyRanField = pluginType.GetField("VetoCommandBodyRan", BindingFlags.Public | BindingFlags.Static)!;

		// Sanity: a non-vetoed plugin command body DOES run, proving plugin commands dispatch in this host.
		var pong = await Cmd("+ping");
		await Assert.That(pong).IsEqualTo("Pong from the sample plugin!");

		await Cmd("+vetome");
		await Assert.That((bool)bodyRanField.GetValue(null)!).IsFalse();
	}

	[Test]
	public async Task ObjectLifecycleHook_OnCreated_FiresForAtCreate()
	{
		// @create fires the OBJECT`CREATE event AND the C# IObjectLifecycleHook.OnCreatedAsync seam; the
		// SamplePlugin's hook records the new object's dbref. Assert it captured the object @create returned.
		var pluginType = FindPluginType("SamplePlugin.SamplePlugin")!;
		var lastCreatedField = pluginType.GetField("LastCreatedObject", BindingFlags.Public | BindingFlags.Static)!;

		var created = await Cmd("@create HookTestObject");
		await Assert.That(created).IsNotNull();

		await Assert.That((string?)lastCreatedField.GetValue(null)).IsEqualTo(created);
	}

	/// <summary>
	/// Resolve a type by full name from the loaded plugin assemblies (which are not linked into this test
	/// project — they load at runtime from <c>plugins/</c>). Falls back to scanning all loaded assemblies.
	/// </summary>
	private Type? FindPluginType(string fullName)
	{
		var catalog = WebAppFactory.Services.GetRequiredService<PluginCatalog>();
		foreach (var plugin in catalog.Plugins)
		{
			var t = plugin.GetType().Assembly.GetType(fullName, throwOnError: false);
			if (t is not null) return t;
		}

		return AppDomain.CurrentDomain.GetAssemblies()
			.Select(a => a.GetType(fullName, throwOnError: false))
			.FirstOrDefault(t => t is not null);
	}
}
