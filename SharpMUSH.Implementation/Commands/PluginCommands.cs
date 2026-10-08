using System.Collections.Immutable;
using MarkupString.Layout;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// <c>@plugin</c>: the plugins this server found, whether each runs now and after the next restart, and turning
	/// one on or off. The same as the Packages page's Plugins tab, through <see cref="IPluginAdministration"/>.
	/// </summary>
	[SharpCommand(Name = "@PLUGIN", Switches = ["LIST", "ENABLE", "DISABLE"], Behavior = CB.Default | CB.NoGagged,
		CommandLock = "PERM^packages.admin", MinArgs = 0, MaxArgs = 1, ParameterNames = ["plugin"])]
	public async ValueTask<Option<CallState>> Plugin(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.ToArray();
		var id = (parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "").Trim();

		MString output = switches switch
		{
			{ Length: > 1 } => MarkupText.Plain("Choose one @plugin operation."),
			["ENABLE" or "DISABLE"] when id.Length == 0 => MarkupText.Plain($"Usage: @plugin/{switches[0].ToLowerInvariant()} <plugin>. See help @plugin."),
			["ENABLE"] => MarkupText.Plain(await PluginSetEnabledAsync(id, true)),
			["DISABLE"] => MarkupText.Plain(await PluginSetEnabledAsync(id, false)),
			_ when id.Length > 0 => await PluginInfoAsync(id),
			_ => await PluginListAsync()
		};

		await NotifyService.Notify(executor, output);
		return new CallState(output);
	}

	private async ValueTask<MString> PluginListAsync()
	{
		var listing = await PluginAdministration.Value.ListAsync(ExecutionBudget.CurrentToken);
		if (listing.Plugins.Count == 0)
		{
			return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Plugins"),
				new TextBlock(MarkupText.Plain("No plugins found."))), 78);
		}

		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Plugin")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Version")) { Wrap = false, Priority = 3 },
				new TableColumn(MarkupText.Plain("From")) { Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("State")) { Min = 10 },
			],
			listing.Plugins.Select(plugin => new[]
			{
				plugin.Id,
				plugin.Version ?? "-",
				plugin.Origin == PluginOrigin.BuiltIn ? "shipped" : plugin.Package is { } package ? $"package {package}" : "installed",
				PluginState(plugin)
			}));
		var parts = listing.RestartPending
			? new Block[] { table, new TextBlock(MarkupText.Plain("Some changes wait for a restart: @shutdown/reboot.")) }
			: [table];
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Plugins"), parts), 78);
	}

	private async ValueTask<MString> PluginInfoAsync(string id)
	{
		var listing = await PluginAdministration.Value.ListAsync(ExecutionBudget.CurrentToken);
		if (listing.Plugins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) is not { } plugin)
		{
			return MarkupText.Plain($"There is no plugin '{id}'. See @plugin/list.");
		}

		(string, MString)[] fields =
		[
			("Plugin", MarkupText.Plain(plugin.Id)),
			("Version", MarkupText.Plain(plugin.Version ?? "-")),
			("From", MarkupText.Plain(plugin.Origin == PluginOrigin.BuiltIn ? "shipped with the server" : plugin.Package is { } package ? $"package {package}" : "the plugins folder")),
			("State", MarkupText.Plain(PluginState(plugin))),
			.. plugin.Reason is { } reason ? new[] { ("Why", MarkupText.Plain(reason)) } : [],
			("Adds", MarkupText.Plain($"{plugin.Commands} commands, {plugin.Functions} functions")),
			.. plugin.Dependents.Count > 0 ? new[] { ("Needed by", MarkupText.Plain(string.Join(", ", plugin.Dependents))) } : [],
			.. plugin.Description is { } description ? new[] { ("About", MarkupText.Plain(description)) } : [],
		];
		return ServerLayout.Build(ServerLayout.Section(MarkupText.Plain(plugin.Name), ServerLayout.KeyValues(fields)), 78);
	}

	private async ValueTask<string> PluginSetEnabledAsync(string id, bool enabled) =>
		await PluginAdministration.Value.SetEnabledAsync(id, enabled, ExecutionBudget.CurrentToken) switch
		{
			PluginChangeResponse change => string.Join(" ", new[]
				{
					change.RemovedPackages.Count > 0 ? $"Uninstalled {string.Join(", ", change.RemovedPackages)}." : null,
					enabled ? $"{change.Plugin.Name} is on." : $"{change.Plugin.Name} is off.",
				}
				.Concat(change.Notes)
				.Where(line => line is not null)),
			Error<string> error => error.Value,
		};

	/// <summary>What a plugin is doing now, and what the next restart changes.</summary>
	private static string PluginState(PluginStatusDto plugin)
	{
		var now = plugin.Status switch
		{
			PluginBootStatus.Loaded => "running",
			PluginBootStatus.Disabled => "off",
			PluginBootStatus.Failed => "failed",
			PluginBootStatus.Incompatible => "incompatible",
			PluginBootStatus.Duplicate => "duplicate",
			_ => "not started"
		};
		return plugin.Pending switch
		{
			PluginPendingChange.Starts => $"{now}; starts at restart",
			PluginPendingChange.Stops => $"{now}; stops at restart",
			_ => now
		};
	}
}
