using System.Globalization;
using MarkupString.Layout;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Utilities;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@PROFILE", Switches = ["START", "STOP"], Behavior = CB.Default,
		MinArgs = 0, MaxArgs = 1, ParameterNames = ["seconds"])]
	public async ValueTask<Option<CallState>> QueueProfile(IMUSHCodeParser parser, SharpCommandAttribute _)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var actor = await parser.ServiceProvider.GetRequiredService<IAdministrativeCapabilityService>()
			.GetGameActorAsync(executor.Object().DBRef, ExecutionBudget.CurrentToken);
		if (actor is null) return await DiagnosticFailure(parser, DiagnosticsError.PermissionDenied);
		var service = parser.ServiceProvider.GetRequiredService<IQueueDiagnosticsService>();
		var switches = parser.CurrentState.Switches.ToArray();
		var argument = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message.ToPlainText() ?? "";
		if (switches.Length > 1 || (switches.Contains("STOP") && argument.Length > 0))
			return await DiagnosticFailure(parser, DiagnosticsError.InvalidRequest);
		if (switches.Contains("START"))
		{
			var seconds = 60;
			if (argument.Length > 0 && (!int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out seconds)
				|| seconds is < 1 or > 300)) return await DiagnosticFailure(parser, DiagnosticsError.InvalidDuration);
			var result = await service.StartProfileAsync(actor, seconds, ExecutionBudget.CurrentToken);
			if (result is DiagnosticsError error) return await DiagnosticFailure(parser, error);
			await NotifyService.NotifyLocalized(executor, "QueueProfileStarted", seconds);
			return CallState.Empty;
		}
		if (switches.Contains("STOP"))
		{
			var result = await service.StopProfileAsync(actor, ExecutionBudget.CurrentToken);
			if (result is DiagnosticsError error) return await DiagnosticFailure(parser, error);
			await NotifyService.NotifyLocalized(executor, "QueueProfileStopped");
			return CallState.Empty;
		}
		if (argument.Length > 0) return await DiagnosticFailure(parser, DiagnosticsError.InvalidRequest);
		await service.CollectProfilesAsync(ExecutionBudget.CurrentToken);
		var report = await service.InspectAsync(actor, ct: ExecutionBudget.CurrentToken);
		if (report is not QueueDiagnosticsReport { Profile: { } profile })
			return await DiagnosticFailure(parser, report is DiagnosticsError error ? error : DiagnosticsError.NotFound);
		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Source")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Kind")) { Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("Name")) { Min = 8 },
				new TableColumn(MarkupText.Plain("Calls")) { Alignment = Alignment.Right, Wrap = false },
				new TableColumn(MarkupText.Plain("Failed")) { Alignment = Alignment.Right, Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("Total ms")) { Alignment = Alignment.Right, Wrap = false },
				new TableColumn(MarkupText.Plain("Max ms")) { Alignment = Alignment.Right, Wrap = false, Priority = 3 },
			],
			profile.Rows.Select(row => (IEnumerable<string>)
			[
				DiagnosticSource(row.Source, row.SourceAttribute), row.Kind, row.Name,
				row.Count.ToString(CultureInfo.InvariantCulture), row.Failures.ToString(CultureInfo.InvariantCulture),
				Milliseconds(row.InclusiveMilliseconds), Milliseconds(row.MaximumMilliseconds)
			]));
		await NotifyService.Notify(executor, ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Queue profile"), table), 78), executor);
		await NotifyService.NotifyLocalized(executor, "QueueProfileReport", profile.ExpiresAt.ToString("u"));
		return CallState.Empty;
	}

	private static string DiagnosticSource(string? source, string? attribute)
		=> (source ?? "?") + (attribute is null ? "" : "/" + attribute);

	private static string Milliseconds(double milliseconds)
		=> milliseconds.ToString("F2", CultureInfo.InvariantCulture);

	private async ValueTask<Option<CallState>> QueueHistory(IMUSHCodeParser parser)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (parser.CurrentState.Switches.Count() != 1) return await DiagnosticFailure(parser, DiagnosticsError.InvalidRequest);
		var argument = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message.ToPlainText() ?? "";
		var limit = 50;
		if (argument.Length > 0 && (!int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out limit)
			|| limit is < 1 or > 100)) return await DiagnosticFailure(parser, DiagnosticsError.InvalidRequest);
		var actor = await parser.ServiceProvider.GetRequiredService<IAdministrativeCapabilityService>()
			.GetGameActorAsync(executor.Object().DBRef, ExecutionBudget.CurrentToken);
		if (actor is null) return await DiagnosticFailure(parser, DiagnosticsError.PermissionDenied);
		return await parser.ServiceProvider.GetRequiredService<IQueueDiagnosticsService>().InspectAsync(actor, limit, ct: ExecutionBudget.CurrentToken) switch
		{
			QueueDiagnosticsReport report => await ReportQueueHistory(executor, report),
			DiagnosticsError error => await DiagnosticFailure(parser, error),
		};
	}

	private async ValueTask<Option<CallState>> ReportQueueHistory(AnySharpObject executor, QueueDiagnosticsReport report)
	{
		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("PID")) { Alignment = Alignment.Right, Wrap = false },
				new TableColumn(MarkupText.Plain("Source")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Owner")) { Wrap = false, Priority = 3 },
				new TableColumn(MarkupText.Plain("Kind")) { Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("Outcome")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Wait ms")) { Alignment = Alignment.Right, Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("Run ms")) { Alignment = Alignment.Right, Wrap = false },
				new TableColumn(MarkupText.Plain("Calls")) { Alignment = Alignment.Right, Wrap = false, Priority = 3 },
			],
			report.Recent.Select(row => (IEnumerable<string>)
			[
				row.Pid?.ToString(CultureInfo.InvariantCulture) ?? "-", DiagnosticSource(row.Source, row.SourceAttribute),
				row.Owner ?? "?", row.Kind, row.Status,
				row.WaitDuration is { } wait ? Milliseconds(wait.TotalMilliseconds) : "-",
				row.ExecutionDuration is { } run ? Milliseconds(run.TotalMilliseconds) : "-",
				row.InvocationCount?.ToString(CultureInfo.InvariantCulture) ?? "-"
			]));
		await NotifyService.Notify(executor, ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Queue history"), table), 78), executor);
		await NotifyService.NotifyLocalized(executor, "QueueHistoryReport");
		return CallState.Empty;
	}

	private async ValueTask<Option<CallState>> DiagnosticFailure(IMUSHCodeParser parser, DiagnosticsError error)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		await NotifyService.NotifyLocalized(executor, "QueueDiagnosticsError", error.ToString());
		return new CallState(error == DiagnosticsError.PermissionDenied ? ErrorMessages.Returns.PermissionDenied
			: "#-1 DIAGNOSTICS " + error.ToString().ToUpperInvariant());
	}
}
