using System.Collections.Immutable;
using System.Globalization;
using MarkupString;
using MarkupString.Layout;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord.Events;
using SharpMUSH.Plugins.Discord.Links;

namespace SharpMUSH.Plugins.Discord.Commands;

/// <summary>
/// <c>@discord/event</c>: the game's events as Discord Scheduled Events, each known by a key the game chooses.
/// <c>@discord/event &lt;key&gt;=&lt;json&gt;</c> publishes or updates one (<see cref="DiscordEventSpec"/>),
/// <c>/start &lt;key&gt;</c> marks it as happening, <c>/end &lt;key&gt;</c> ends or cancels it, and <c>@discord/event</c> alone
/// lists what is published. The caller has <c>discord.admin</c> already. Each change returns the key at once and tells
/// the caller how it went when Discord answers.
/// </summary>
public static class DiscordEventCommands
{
	private const string Badge = "DISCORD";

	/// <param name="left">What is left of the <c>=</c>, or all of it.</param>
	/// <param name="right">What is right of the <c>=</c>, or null when there is none.</param>
	public static async ValueTask<Option<CallState>> RunAsync(IMUSHCodeParser parser, AnySharpObject executor, string[] switches,
		string left, string? right)
	{
		var services = parser.ServiceProvider;
		var notify = services.GetRequiredService<INotifyService>();
		var events = services.GetRequiredService<DiscordScheduledEvents>();
		if (left.Length == 0)
		{
			if (right is not null || switches.Contains("START") || switches.Contains("END"))
			{
				await notify.Notify(executor, Notice("@discord/event needs a key: @discord/event <key>=<json>.", NoticeKind.Warn));
				return new CallState("#-1 NO KEY");
			}

			await notify.Notify(executor, List(services.GetRequiredService<IDiscordEventStore>().List()));
			return CallState.Empty;
		}

		Func<CancellationToken, Task<Result<string>>> change;
		if (switches.Contains("START"))
		{
			change = cancellationToken => events.StartAsync(left, cancellationToken);
		}
		else if (switches.Contains("END"))
		{
			change = cancellationToken => events.EndAsync(left, cancellationToken);
		}
		else if (right is null)
		{
			await notify.Notify(executor, Notice("@discord/event <key>=<json> publishes an event; /start and /end change it.", NoticeKind.Warn));
			return new CallState("#-1 NO EVENT");
		}
		else
		{
			switch (DiscordEventSpec.Parse(right, services.GetRequiredService<TimeProvider>().GetUtcNow()))
			{
				case DiscordEventSpec spec:
					change = cancellationToken => events.PublishAsync(left, spec, cancellationToken);
					break;
				case Error<string> error:
					await notify.Notify(executor, Notice(error.Value, NoticeKind.Warn));
					return new CallState("#-1 BAD EVENT");
				default:
					return new CallState("#-1 BAD EVENT");
			}
		}

		var logger = services.GetRequiredService<ILogger<DiscordRelay>>();
		// Told outside this command's queue entry, as linking a channel is.
		using (ExecutionContext.SuppressFlow())
		{
			_ = Task.Run(() => DiscordCommandModule.AnswerAsync(notify, executor, logger, async cancellationToken =>
				await change(cancellationToken) switch
				{
					string done => Notice(done, NoticeKind.Ok),
					Error<string> error => Notice(error.Value, NoticeKind.Error),
				}));
		}

		return new CallState(left);
	}

	private static MString List(IReadOnlyList<DiscordEventRecord> published)
	{
		Block body = published.Count == 0
			? ServerLayout.Body(MarkupText.Plain("Nothing is published. @discord/event <key>=<json> publishes an event."))
			: ServerLayout.Listing(
				[
					new TableColumn(MarkupText.Plain("Key")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Event")),
					new TableColumn(MarkupText.Plain("Starts (UTC)")) { Wrap = false, Priority = 2 },
				],
				published.OrderBy(record => record.Start).Select(record => ImmutableArray.Create(record.Key, record.Name,
					record.Start.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).AsEnumerable()));
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Discord events"), body), 78);
	}

	private static MString Notice(string text, NoticeKind kind) => NoticeMarkup.Message(Badge, text, kind);
}
