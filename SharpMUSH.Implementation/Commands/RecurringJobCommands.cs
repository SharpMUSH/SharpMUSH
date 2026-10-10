using System.Globalization;
using MarkupString.Layout;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models.RecurringJobs;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Services.RecurringJobs;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@JOB", Switches = ["CREATE", "LIST", "ALL", "DISABLE", "ENABLE", "DELETE", "SCHEDULE"],
		Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged, MinArgs = 0, MaxArgs = 2,
		ParameterNames = ["target/attribute or job-id", "schedule|timezone|description"])]
	public async ValueTask<Option<CallState>> RecurringJob(IMUSHCodeParser parser, SharpCommandAttribute command)
	{
		var ct = ExecutionBudget.CurrentToken;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		MString output = MarkupText.Empty;
		try
		{
			var actor = await parser.ServiceProvider.GetRequiredService<IAdministrativeCapabilityService>().GetGameActorAsync(executor.Object().DBRef, ct)
				?? throw new RecurringJobException("denied", "A linked active player is required.");
			var service = parser.ServiceProvider.GetRequiredService<IRecurringJobService>();
			var switches = parser.CurrentState.Switches;
			var operation = switches.Where(s => s is "CREATE" or "LIST" or "DISABLE" or "ENABLE" or "DELETE" or "SCHEDULE").ToArray();
			if (operation.Length != 1) throw new RecurringJobException("invalid", "Choose /create, /list, /disable, /enable, /delete or /schedule.");
			var lhs = parser.CurrentState.Arguments.TryGetValue("0", out var left) ? left.Message.ToPlainText() : "";
			var rhs = parser.CurrentState.Arguments.TryGetValue("1", out var right) ? right.Message.ToPlainText() : "";
			if (operation[0] == "LIST")
			{
				var jobs = await service.ListAsync(actor, switches.Contains("ALL"), ct);
				output = jobs.Length == 0 ? MarkupText.Plain("No recurring jobs.") : RecurringJobListing(jobs);
			}
			else if (operation[0] == "CREATE")
			{
				var target = lhs.Split('/', 2);
				var schedule = rhs.Split('|', 3);
				if (target.Length != 2 || schedule.Length < 2) throw new RecurringJobException("invalid", "Use target/attribute=schedule|timezone|description.");
				// The job stores the full objid, so a recycled dbref can't take it over.
				if (await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, target[0].Trim(), LocateFlags.All) is not AnySharpObject found)
					return new CallState(ErrorMessages.Returns.NoMatch);
				var job = await service.CreateAsync(actor, new(found.Object().DBRef.ToString(), target[1].Trim(), schedule[0].Trim(), schedule[1].Trim(), schedule.Length == 3 ? schedule[2].Trim() : ""), ct);
				output = MarkupText.Plain("Created recurring job " + job.Id);
			}
			else
			{
				var job = (await service.ListAsync(actor, switches.Contains("ALL"), ct)).SingleOrDefault(j => j.Id == lhs)
					?? throw new RecurringJobException("missing", "Job not found.");
				if (operation[0] == "DELETE")
				{
					await service.DeleteAsync(actor, job.Id, ct);
					output = MarkupText.Plain("Recurring job deleted.");
				}
				else
				{
					var schedule = operation[0] == "SCHEDULE" ? rhs.Split('|', 2) : [job.Schedule, job.TimeZone];
					if (schedule.Length != 2) throw new RecurringJobException("invalid", "Use job-id=schedule|timezone.");
					await service.ConfigureAsync(actor, job.Id, schedule[0].Trim(), schedule[1].Trim(), operation[0] == "ENABLE" || operation[0] == "SCHEDULE" && job.Enabled, ct);
					output = MarkupText.Plain("Recurring job updated.");
				}
			}
		}
		catch (RecurringJobException ex) { output = MarkupText.Plain("#-1 " + ex.Message); }
		await NotifyService.Notify(executor, output);
		return new CallState(output);
	}

	/// <summary>
	/// The jobs, each under a divider carrying its id: an id is the whole of what <c>/disable</c> and the
	/// rest take, too long to share a table row with the schedule and the next run.
	/// </summary>
	private static MString RecurringJobListing(RecurringJob[] jobs)
	{
		var parts = jobs.SelectMany(job => new Block[]
		{
			new Rule(MarkupText.Plain(job.Id)) { TitleAlignment = Alignment.Left },
			ServerLayout.KeyValues(JobFields(job)),
		});
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Recurring jobs"), [.. parts]), 78);
	}

	private static IEnumerable<(string Label, MString Value)> JobFields(RecurringJob job)
	{
		if (job.Description.Length > 0) yield return ("Description", MarkupText.Plain(job.Description));
		yield return ("Runs", MarkupText.Plain($"{job.Target}/{job.Attribute}"));
		yield return ("Schedule", MarkupText.Plain($"{job.Schedule} {job.TimeZone}"));
		yield return ("Status", MarkupText.Plain(job.Status));
		yield return ("Next run", MarkupText.Plain(JobTime(job.NextRun)));
		yield return ("Last run", MarkupText.Plain(JobTime(job.LastRun)));
		if (!string.IsNullOrEmpty(job.LastError)) yield return ("Last error", MarkupText.Plain(job.LastError));
	}

	private static string JobTime(long? unixMilliseconds)
		=> unixMilliseconds is { } at
			? DateTimeOffset.FromUnixTimeMilliseconds(at).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
			: "-";
}
