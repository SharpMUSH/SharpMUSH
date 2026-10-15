using System.Collections.Immutable;
using System.Globalization;
using MarkupString;
using MarkupString.Layout;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord.Roles;

namespace SharpMUSH.Plugins.Discord.Commands;

/// <summary>
/// <c>@discord/map</c>, <c>/unmap</c> and <c>/drift</c>: game roles mirrored onto Discord roles, and the changes made to
/// them by hand on Discord. The caller has <c>discord.admin</c> already.
/// </summary>
public static class DiscordRoleCommands
{
	private const string Badge = "DISCORD";

	/// <param name="left">What is left of the <c>=</c>, or all of it.</param>
	/// <param name="right">What is right of the <c>=</c>, or null when there is none.</param>
	public static async ValueTask<Option<CallState>> RunAsync(IMUSHCodeParser parser, AnySharpObject executor, string[] switches,
		string left, string? right)
	{
		var services = parser.ServiceProvider;
		var notify = services.GetRequiredService<INotifyService>();
		var maps = services.GetRequiredService<DiscordRoleMaps>();
		var cancellationToken = ExecutionBudget.CurrentToken;
		var (answer, result) = switches switch
		{
			_ when switches.Contains("DRIFT") && switches.Contains("APPLY") => Settle(services, maps, left),
			_ when switches.Contains("DRIFT") => (Drift(services, maps), CallState.Empty),
			_ when switches.Contains("UNMAP") => Done(await maps.UnmapAsync(left, cancellationToken), mapping =>
				$"{mapping.RoleName} is no longer mapped to {mapping.Slug}. Members keep it as it is."),
			_ when switches.Contains("ENFORCE") || switches.Contains("REPORT") => Done(
				await maps.ModeAsync(left, switches.Contains("ENFORCE") ? DiscordRoleMode.Enforce : DiscordRoleMode.Report, cancellationToken),
				mapping => mapping.Mode == DiscordRoleMode.Enforce
					? $"A change to {mapping.RoleName} made by hand on Discord is put back at once, and still listed by @discord/drift."
					: $"A change to {mapping.RoleName} made by hand on Discord is listed by @discord/drift and left for you to decide."),
			_ when right is null && left.Length == 0 => (List(maps.List()), CallState.Empty),
			_ when right is null => (Notice("@discord/map <game role>=<Discord role> maps one; @discord/map alone lists them.", NoticeKind.Warn),
				new CallState("#-1")),
			_ => Mapped(await maps.MapAsync(
				(await services.GetRequiredService<IAdministrativeCapabilityService>().GetObjectGrantsAsync(executor, cancellationToken)).Context,
				executor.Object().DBRef.ToString(), left, right, switches.Contains("CONFIRM"), cancellationToken)),
		};

		await notify.Notify(executor, answer);
		return result;
	}

	private static (MString, CallState) Mapped(Result<DiscordRoleMappingStatus> result) => result switch
	{
		DiscordRoleMappingStatus { Problem: { } problem } status => (Notice(
			$"{status.Mapping.RoleName} is mapped to {status.Mapping.Slug}, but nothing changes on Discord until it is fixed: {problem}.",
			NoticeKind.Warn), Id(status.Mapping)),
		DiscordRoleMappingStatus status => (Notice(
			$"{status.Mapping.RoleName} is mapped to {status.Mapping.Slug}. Linked members whose account holds {status.Mapping.Slug} get it, and anyone else loses it. Members who are not linked keep it for now and are listed by @discord/drift.",
			NoticeKind.Ok), Id(status.Mapping)),
		Error<string> error => (Notice(error.Value, NoticeKind.Error), new CallState("#-1")),
	};

	private static (MString, CallState) Done(Result<DiscordRoleMapping> result, Func<DiscordRoleMapping, string> text) => result switch
	{
		DiscordRoleMapping mapping => (Notice(text(mapping), NoticeKind.Ok), Id(mapping)),
		Error<string> error => (Notice(error.Value, NoticeKind.Warn), new CallState("#-1")),
	};

	private static (MString, CallState) Settle(IServiceProvider services, DiscordRoleMaps maps, string role)
	{
		var discord = services.GetRequiredService<IDiscordRoles>();
		if (role.Length == 0)
		{
			discord.Settle(null);
			return (Notice("Putting every role changed by hand back to the game's roles.", NoticeKind.Ok), CallState.Empty);
		}

		if (maps.Lookup(role) is not { } mapping)
		{
			return (Notice($"{role} is not a mapped Discord role. @discord/map lists them.", NoticeKind.Warn), new CallState("#-1"));
		}

		discord.Settle(mapping.RoleId);
		return (Notice($"Putting {mapping.RoleName} back to the game's roles wherever it was changed by hand.", NoticeKind.Ok), Id(mapping));
	}

	private static MString List(IReadOnlyList<DiscordRoleMappingStatus> mappings)
	{
		Block body = mappings.Count == 0
			? ServerLayout.Body(MarkupText.Plain("No roles are mapped. @discord/map <game role>=<Discord role> maps one."))
			: ServerLayout.Listing(
				[
					new TableColumn(MarkupText.Plain("Discord role")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Game role")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Id")) { Wrap = false, Priority = 3 },
					new TableColumn(MarkupText.Plain("By hand")) { Wrap = false, Priority = 2 },
					new TableColumn(MarkupText.Plain("State")) { Min = 17 },
				],
				mappings.Select(status => ImmutableArray.Create(
					"@" + status.Mapping.RoleName,
					status.Mapping.Slug,
					status.Mapping.RoleId.ToString(CultureInfo.InvariantCulture),
					status.Mapping.Mode == DiscordRoleMode.Enforce ? "put back" : "reported",
					status.Problem is { } problem ? $"broken: {problem}" : status.Drift > 0 ? $"{status.Drift} changed by hand" : "applied")
					.AsEnumerable()));
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Discord roles"), body), 78);
	}

	private static MString Drift(IServiceProvider services, DiscordRoleMaps maps)
	{
		var store = services.GetRequiredService<IDiscordRoleStore>();
		var rows = store.Drift()
			.SelectMany(record => record.Members.Select(drift => (Mapping: store.Mapping(record.RoleId), Drift: drift)))
			.Where(row => row.Mapping is not null)
			.OrderBy(row => row.Drift.At)
			.ToList();
		Block body = rows.Count == 0
			? ServerLayout.Body(MarkupText.Plain("No mapped role was changed by hand."))
			: ServerLayout.Listing(
				[
					new TableColumn(MarkupText.Plain("Member")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Discord role")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Change")) { Min = 10 },
					new TableColumn(MarkupText.Plain("Seen")) { Wrap = false, Priority = 2 },
				],
				rows.Select(row => ImmutableArray.Create(
					row.Drift.UserName,
					"@" + row.Mapping!.RoleName,
					row.Drift.Added ? $"given it, without {row.Mapping.Slug} in the game" : $"stripped of it, with {row.Mapping.Slug} in the game",
					row.Drift.At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).AsEnumerable()));
		var hint = MarkupText.Plain(
			"@discord/drift/apply [<role>] puts the game's roles back; @discord/unmap <role> stops mapping the role instead.");
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Discord roles changed by hand"), body, ServerLayout.Body(hint)), 78);
	}

	private static CallState Id(DiscordRoleMapping mapping) => new(mapping.RoleId.ToString(CultureInfo.InvariantCulture));

	private static MString Notice(string text, NoticeKind kind) => NoticeMarkup.Message(Badge, text, kind);
}
