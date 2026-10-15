using System.Globalization;
using MarkupString.Layout;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Webhooks;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>The most private events a webhook whose events <paramref name="executor"/> sets may be sent.</summary>
	private static async ValueTask<EventPrivacy> WebhookReachAsync(AnySharpObject executor)
		=> WebhookSubscription.ReachOf(await executor.Can(PortalPermission.GamePower("See_All")),
			await executor.Can(PortalPermission.PlayersModerate));

	private static string WebhookWithheld(EventPrivacy reach) => reach switch
	{
		EventPrivacy.Private => "",
		EventPrivacy.Staff => "\nIt is not sent private events: you may not read them.",
		_ => "\nIt is sent public events only: you may not see staff or private ones."
	};

	private static readonly string[] WebhookOperations =
		["LIST", "CREATE", "URL", "EVENTS", "ENABLE", "DISABLE", "ROTATE", "TEST", "DELETE"];

	/// <summary>
	/// <c>@webhook</c>: endpoints outside the game that hear its events (<c>help @webhook</c>). A SharpMUSH
	/// command; PennMUSH has no outbound events.
	/// </summary>
	[SharpCommand(Name = "@WEBHOOK", Switches = ["LIST", "CREATE", "URL", "EVENTS", "ENABLE", "DISABLE", "ROTATE", "TEST", "DELETE"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, CommandLock = "PERM^config.admin",
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["webhook", "value"])]
	public async ValueTask<Option<CallState>> Webhook(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var operations = parser.CurrentState.Switches.Where(WebhookOperations.Contains).ToArray();
		var args = parser.CurrentState.Arguments;
		var name = (args.GetValueOrDefault("0")?.Message.ToPlainText() ?? "").Trim();
		var value = (args.GetValueOrDefault("1")?.Message.ToPlainText() ?? "").Trim();
		var executorRef = executor.Object().DBRef;
		var author = new SecretAuthor(executorRef.ToString(), executor.Object().Name);

		var operation = operations.Length switch
		{
			0 => name.Length == 0 ? "LIST" : "INFO",
			1 => operations[0],
			_ => ""
		};

		if (operation is not ("LIST" or "") && name.Length == 0)
		{
			return await WebhookReplyAsync(executor, "Which webhook?", ok: false);
		}

		return operation switch
		{
			"" => await WebhookReplyAsync(executor, "Choose one @webhook operation.", ok: false),
			"LIST" => await WebhookReplyAsync(executor, WebhookList(await Webhooks.ListAsync()), name),
			"INFO" => await Webhooks.GetAsync(name) switch
			{
				WebhookSubscription found => await WebhookReplyAsync(executor, WebhookInfo(found, Webhooks.Status(found.Name)), found.Name),
				NotFound => await WebhookReplyAsync(executor, $"There is no webhook called {name}.", ok: false),
			},
			"CREATE" => await Webhooks.CreateAsync(name, value, [], EventPrivacy.Public, executorRef.ToString(), author) switch
			{
				WebhookIssued created => await WebhookChangedAsync(executor, AuditActions.WebhookCreate, created.Subscription,
					$"Webhook {created.Subscription.Name} created. It hears no events yet: set them with @webhook/events {created.Subscription.Name}=<pattern> ...\n"
					+ WebhookSecretSaid(created), $"url {WebhookAddress.Redacted(created.Subscription.Url)}"),
				Error<string> error => await WebhookReplyAsync(executor, error.Value, ok: false),
			},
			"URL" => await WebhookChangeAsync(executor, name, await Webhooks.SetUrlAsync(name, value),
				changed => changed.Url is null
					? $"Webhook {changed.Name} has no address now: it is heard only through its event stream."
					: $"Webhook {changed.Name} now posts to {changed.Url}.", $"url {WebhookAddress.Redacted(value)}"),
			"EVENTS" => await WebhookChangeAsync(executor, name,
				await Webhooks.SetEventsAsync(name, value.Split(' ', StringSplitOptions.RemoveEmptyEntries), await WebhookReachAsync(executor)),
				changed => changed.Events.Count == 0
					? $"Webhook {changed.Name} hears no events now."
					: $"Webhook {changed.Name} now hears: {string.Join(' ', changed.Events)}{WebhookWithheld(changed.Reach)}", $"events {value}"),
			"ENABLE" => await WebhookChangeAsync(executor, name, await Webhooks.SetEnabledAsync(name, true),
				changed => $"Webhook {changed.Name} is on.", "enabled"),
			"DISABLE" => await WebhookChangeAsync(executor, name, await Webhooks.SetEnabledAsync(name, false),
				changed => $"Webhook {changed.Name} is off.", "disabled"),
			"ROTATE" => await Webhooks.RotateSecretAsync(name, author) switch
			{
				WebhookIssued rotated => await WebhookChangedAsync(executor, AuditActions.WebhookChange, rotated.Subscription,
					$"Webhook {rotated.Subscription.Name}'s old signing secret no longer verifies.\n{WebhookSecretSaid(rotated)}",
					"secret rotated"),
				NotFound => await WebhookReplyAsync(executor, $"There is no webhook called {name}.", ok: false),
				Error<string> error => await WebhookReplyAsync(executor, error.Value, ok: false),
			},
			"TEST" => await Webhooks.TestAsync(name, executorRef) switch
			{
				WebhookMessage message => await WebhookReplyAsync(executor,
					$"Sent {message.EventName} to webhook {name} as {message.Id}. @webhook {name} shows how it went.", name),
				NotFound => await WebhookReplyAsync(executor, $"There is no webhook called {name}.", ok: false),
				Error<string> error => await WebhookReplyAsync(executor, error.Value, ok: false),
			},
			"DELETE" => await Webhooks.DeleteAsync(name) switch
			{
				WebhookSubscription removed => await WebhookChangedAsync(executor, AuditActions.WebhookDelete, removed,
					$"Webhook {removed.Name} deleted.", null),
				NotFound => await WebhookReplyAsync(executor, $"There is no webhook called {name}.", ok: false),
			},
			_ => await WebhookReplyAsync(executor, "Choose one @webhook operation.", ok: false),
		};
	}

	/// <summary>The new secret, said once: it is in the secret store from now on and never shown again.</summary>
	private static string WebhookSecretSaid(WebhookIssued issued) => issued switch
	{
		{ OverriddenBy: { } overriddenBy } => $"Its signing secret is set by {overriddenBy}, which the server signs with instead.",
		_ => $"Its signing secret is {issued.Secret}\nCopy it now: it is not shown again. @webhook/rotate makes a new one.",
	};

	private async ValueTask<Option<CallState>> WebhookChangeAsync(AnySharpObject executor, string name,
		FoundResult<WebhookSubscription> result, Func<WebhookSubscription, string> said, string details)
		=> result switch
		{
			WebhookSubscription changed => await WebhookChangedAsync(executor, AuditActions.WebhookChange, changed, said(changed), details),
			NotFound => await WebhookReplyAsync(executor, $"There is no webhook called {name}.", ok: false),
			Error<string> error => await WebhookReplyAsync(executor, error.Value, ok: false),
		};

	private async ValueTask<Option<CallState>> WebhookChangedAsync(AnySharpObject executor, string action,
		WebhookSubscription subscription, string said, string? details)
	{
		await Audit.RecordAsync(executor, action, new AuditTarget(AuditTargetKinds.Webhook, subscription.Name, subscription.Name), details);
		return await WebhookReplyAsync(executor, said, subscription.Name);
	}

	private async ValueTask<Option<CallState>> WebhookReplyAsync(AnySharpObject executor, string text, bool ok)
		=> await WebhookReplyAsync(executor, MarkupText.Plain(text), ok ? "" : "#-1");

	private async ValueTask<Option<CallState>> WebhookReplyAsync(AnySharpObject executor, string text, string result)
		=> await WebhookReplyAsync(executor, MarkupText.Plain(text), result);

	private async ValueTask<Option<CallState>> WebhookReplyAsync(AnySharpObject executor, MString text, string result)
	{
		await NotifyService.Notify(executor, text, executor);
		return new CallState(result);
	}

	private static MString WebhookList(IReadOnlyList<WebhookSubscription> subscriptions)
	{
		if (subscriptions.Count == 0)
		{
			return MarkupText.Plain("There are no webhooks. @webhook/create <name>=<url> adds one.");
		}

		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Webhooks"), ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Name")) { Wrap = false },
				new TableColumn(MarkupText.Plain("State")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Events")) { Min = 10 },
				new TableColumn(MarkupText.Plain("Address")) { Min = 10 },
			],
			subscriptions.OrderBy(subscription => subscription.Name, StringComparer.OrdinalIgnoreCase)
				.Select(subscription => (IEnumerable<string>)
				[
					subscription.Name,
					subscription.Enabled ? "on" : "off",
					subscription.Events.Count == 0 ? "none" : string.Join(' ', subscription.Events),
					subscription.Url ?? "stream only",
				]))), 78);
	}

	private static MString WebhookInfo(WebhookSubscription subscription, WebhookStatus status)
	{
		(string, MString)[] fields =
		[
			("State", MarkupText.Plain(subscription.Enabled ? "on" : $"off{(subscription.DisabledReason is { } why ? $": {why}" : "")}")),
			("Address", MarkupText.Plain(subscription.Url ?? "none: heard only through its event stream")),
			("Events", MarkupText.Plain(subscription.Events.Count == 0 ? "none" : string.Join(' ', subscription.Events))),
			("Sent", MarkupText.Plain(subscription.Reach switch
			{
				EventPrivacy.Private => "every event it hears",
				EventPrivacy.Staff => "public and staff events, not private ones",
				_ => "public events only"
			})),
			("Secret", MarkupText.Plain(WebhookSubscription.SecretName(subscription.Name))),
			("Created", MarkupText.Plain($"{WebhookTime(subscription.CreatedAt)}{(subscription.CreatedBy is { } by ? $" by {by}" : "")}")),
			("Delivered", MarkupText.Plain($"{status.Delivered.ToString(CultureInfo.InvariantCulture)}{(status.LastDelivered is { } at ? $", last {WebhookTime(at)}" : "")}")),
			("Failed tries", MarkupText.Plain($"{status.FailedAttempts.ToString(CultureInfo.InvariantCulture)}{(status.LastFailed is { } failed ? $", last {WebhookTime(failed)}" : "")}")),
			("Dropped", MarkupText.Plain(status.Dropped.ToString(CultureInfo.InvariantCulture))),
			("Waiting", MarkupText.Plain(status.Pending.ToString(CultureInfo.InvariantCulture))),
			("Streams", MarkupText.Plain(status.Streams.ToString(CultureInfo.InvariantCulture))),
		];

		List<Block> parts = [ServerLayout.KeyValues(fields)];
		if (status.Recent.Count > 0)
		{
			parts.Add(new Rule(MarkupText.Plain("Recent tries")));
			parts.Add(ServerLayout.Listing(
				[
					new TableColumn(MarkupText.Plain("Time")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Event")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Try")) { Alignment = Alignment.Right, Wrap = false },
					new TableColumn(MarkupText.Plain("Result")) { Min = 10 },
				],
				status.Recent.Select(attempt => (IEnumerable<string>)
				[
					WebhookTime(attempt.At),
					attempt.EventName,
					attempt.Attempt.ToString(CultureInfo.InvariantCulture),
					WebhookResult(attempt),
				])));
		}

		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain($"Webhook {subscription.Name}"), [.. parts]), 78);
	}

	private static string WebhookResult(WebhookAttempt attempt) => attempt.Outcome switch
	{
		WebhookAttemptOutcome.Delivered => $"delivered, {attempt.StatusCode}, {attempt.Duration.TotalMilliseconds:0}ms",
		WebhookAttemptOutcome.Retrying => $"{attempt.Error}; will retry",
		_ => $"{attempt.Error}; dropped",
	};

	private static string WebhookTime(DateTimeOffset at)
		=> at.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
