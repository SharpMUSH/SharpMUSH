using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Utilities;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

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
		var argument = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "";
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
		var rows = string.Join('\n', profile.Rows.Select(row =>
			$"{row.Source ?? "?"}{(row.SourceAttribute is null ? "" : "/" + row.SourceAttribute)} {row.Kind} {row.Name} " +
			$"{row.Count} {row.Failures} {row.InclusiveMilliseconds.ToString("F2", CultureInfo.InvariantCulture)} {row.MaximumMilliseconds.ToString("F2", CultureInfo.InvariantCulture)}"));
		await NotifyService.NotifyLocalized(executor, "QueueProfileReport", profile.ExpiresAt.ToString("u"), rows);
		return CallState.Empty;
	}

	private async ValueTask<Option<CallState>> QueueHistory(IMUSHCodeParser parser)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (parser.CurrentState.Switches.Count() != 1) return await DiagnosticFailure(parser, DiagnosticsError.InvalidRequest);
		var argument = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "";
		var limit = 50;
		if (argument.Length > 0 && (!int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out limit)
			|| limit is < 1 or > 100)) return await DiagnosticFailure(parser, DiagnosticsError.InvalidRequest);
		var actor = await parser.ServiceProvider.GetRequiredService<IAdministrativeCapabilityService>()
			.GetGameActorAsync(executor.Object().DBRef, ExecutionBudget.CurrentToken);
		if (actor is null) return await DiagnosticFailure(parser, DiagnosticsError.PermissionDenied);
		var result = await parser.ServiceProvider.GetRequiredService<IQueueDiagnosticsService>().InspectAsync(actor, limit, ct: ExecutionBudget.CurrentToken);
		if (result is DiagnosticsError error) return await DiagnosticFailure(parser, error);
		var rows = string.Join('\n', ((QueueDiagnosticsReport)result.Value!).Recent.Select(row =>
			$"{row.Pid?.ToString() ?? "-"} {row.Source ?? "?"}{(row.SourceAttribute is null ? "" : "/" + row.SourceAttribute)} {row.Owner ?? "?"} {row.Kind} {row.Status} " +
			$"{row.WaitDuration?.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture) ?? "-"} " +
			$"{row.ExecutionDuration?.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture) ?? "-"} {row.InvocationCount}"));
		await NotifyService.NotifyLocalized(executor, "QueueHistoryReport", rows);
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
