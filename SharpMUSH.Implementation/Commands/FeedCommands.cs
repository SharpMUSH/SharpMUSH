using System.Globalization;
using MarkupString.Layout;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private static readonly string[] FeedOperations =
	[
		"LIST", "INFO", "DEFINE", "UNDEFINE", "DESCRIBE", "SET", "LOCK", "UNLOCK", "TAP", "UNTAP", "PURGE", "DELETE",
		"JOIN", "LEAVE", "GAG", "UNGAG", "SEEN", "WHO", "SEND"
	];

	private static readonly string[] FeedStyleSwitches = ["SAY", "POSE", "SEMIPOSE", "EMIT", "ANNOUNCE"];

	/// <summary>The longest <c>max_age</c> a feed takes: ten years.</summary>
	private const long MaxFeedAgeSeconds = 10L * 365 * 86400;

	/// <summary>A feed named in a command: its kind, and the feed itself (new when it has no row yet).</summary>
	private sealed record FeedTarget(SharpFeedKind Kind, SharpFeed Feed);

	/// <summary>
	/// <c>@feed</c>: the feed pipeline (<c>help @feed</c>). Kinds, their settings, locks and taps need
	/// <c>feed.admin</c>; one feed's members, lines, settings and locks need control of the kind's owner (or
	/// <c>feed.admin</c>), and are meant to be run by a system's own code, never typed by players.
	/// </summary>
	[SharpCommand(Name = "@FEED",
		Switches = ["LIST", "INFO", "DEFINE", "UNDEFINE", "DESCRIBE", "SET", "LOCK", "UNLOCK", "TAP", "UNTAP", "PURGE",
			"DELETE", "JOIN", "LEAVE", "GAG", "UNGAG", "SEEN", "WHO", "SEND", "TO", "AS", "SAY",
			"POSE", "SEMIPOSE", "EMIT", "ANNOUNCE"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 2, ParameterNames = ["feed", "value"])]
	public async ValueTask<Option<CallState>> Feed(IMUSHCodeParser parser, SharpCommandAttribute _)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.ToArray();
		var operations = switches.Where(FeedOperations.Contains).ToArray();
		var styles = switches.Where(FeedStyleSwitches.Contains).ToArray();
		var args = parser.CurrentState.Arguments;
		var left = (args.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "").Trim();
		var rightText = args.GetValueOrDefault("1")?.Message ?? MarkupText.Empty;
		var right = rightText.ToPlainText().Trim();

		var operation = operations.Length switch
		{
			0 when styles.Length > 0 || switches.Contains("TO") || switches.Contains("AS") => "SEND",
			0 => left.Length == 0 ? "LIST" : "INFO",
			1 => operations[0],
			_ => ""
		};

		MString output;
		if (operation.Length == 0)
			output = MarkupText.Plain("Choose one @feed operation.");
		else if (styles.Length > 1)
			output = MarkupText.Plain("Choose one style: /say, /pose, /semipose, /emit or /announce.");
		else if (operation != "SEND" && (styles.Length > 0 || switches.Contains("TO") || switches.Contains("AS")))
			output = MarkupText.Plain("Styles, /to and /as go with @feed/send.");
		else
		{
			output = operation switch
			{
				"LIST" => await FeedListAsync(parser, executor),
				"INFO" => await FeedInfoAsync(parser, executor, left),
				"WHO" => await FeedTargetAsync(parser, executor, left) switch
				{
					FeedTarget target => await FeedWhoAsync(target),
					Error<string> error => MarkupText.Plain(error.Value)
				},
				"SEND" => MarkupText.Plain(await FeedTargetAsync(parser, executor, left) switch
				{
					FeedTarget target => await FeedSendAsync(parser, executor, target, rightText,
						styles.FirstOrDefault()?.ToLowerInvariant(), switches.Contains("TO"), switches.Contains("AS")),
					Error<string> error => error.Value
				}),
				"JOIN" or "LEAVE" or "GAG" or "UNGAG" or "SEEN"
					=> MarkupText.Plain(await FeedTargetAsync(parser, executor, left) switch
					{
						FeedTarget target => await FeedMemberChangeAsync(parser, executor, operation, target, right),
						Error<string> error => error.Value
					}),
				"DELETE" => MarkupText.Plain(await FeedTargetAsync(parser, executor, left) switch
				{
					FeedTarget target => await FeedDeleteAsync(target),
					Error<string> error => error.Value
				}),
				"PURGE" => MarkupText.Plain(await FeedTargetAsync(parser, executor, left) switch
				{
					FeedTarget target => await FeedPurgeAsync(target, right),
					Error<string> error => error.Value
				}),
				"SET" or "LOCK" or "UNLOCK" => MarkupText.Plain(await FeedOptionAsync(parser, executor, operation, left, right)),
				_ => MarkupText.Plain(await FeedKindChangeAsync(parser, executor, operation, left, right))
			};
		}

		if (output.Length > 0) await NotifyService.Notify(executor, output);
		return new CallState(output);
	}

	private static IFeedService Feeds(IMUSHCodeParser parser) => parser.ServiceProvider.GetRequiredService<IFeedService>();

	private static ILockService Locks(IMUSHCodeParser parser) => parser.ServiceProvider.GetRequiredService<ILockService>();

	/// <summary>
	/// <c>&lt;kind&gt;/&lt;key&gt;</c>, with the kind found and the executor allowed to run it (control of its
	/// owner, or <c>feed.admin</c>).
	/// </summary>
	private async ValueTask<Result<FeedTarget>> FeedTargetAsync(IMUSHCodeParser parser, AnySharpObject executor,
		string text)
	{
		if (!FeedNames.TryParse(text, out var kindName, out var key) || key is null)
			return new Error<string>($"'{text}' is not a feed: give <kind>/<key>, such as radio/101.5.");
		if (await Feeds(parser).GetKindAsync(kindName) is not SharpFeedKind kind)
			return new Error<string>($"No feed kind named '{kindName}'. See @feed/list.");
		if (!await Feeds(parser).CanRunAsync(executor, kind))
			return new Error<string>($"Running feed kind '{kind.Name}' needs control of its owner or the {PortalPermission.FeedAdmin} permission.");

		var feed = await Mediator.Send(new GetFeedQuery(kind.Name, key), ExecutionBudget.CurrentToken) switch
		{
			SharpFeed found => found,
			_ => SharpFeed.New(kind.Name, key)
		};
		return new FeedTarget(kind, feed);
	}

	/// <summary>Finds an object by name or dbref, without telling the executor when nothing matches.</summary>
	private async ValueTask<Result<AnySharpObject>> FeedObjectAsync(IMUSHCodeParser parser, AnySharpObject executor,
		string name)
		=> name.Length == 0
			? new Error<string>("Name an object.")
			: await LocateService.Locate(parser, executor, executor, name, LocateFlags.All) switch
			{
				AnySharpObject found => found,
				Error<string> error => new Error<string>(error.Value),
				_ => new Error<string>($"I can't find '{name}'.")
			};

	private static string Display(AnySharpObject obj) => $"{obj.Object().Name}({obj.Object().DBRef.Number.ToString(CultureInfo.InvariantCulture)})";

	private async ValueTask<string> DisplayAsync(DBRef dbref)
		=> await Mediator.Send(new GetObjectNodeQuery(dbref), ExecutionBudget.CurrentToken) is AnySharpObject obj
			? Display(obj)
			: $"{dbref} (gone)";

	private async ValueTask<MString> FeedListAsync(IMUSHCodeParser parser, AnySharpObject executor)
	{
		var ct = ExecutionBudget.CurrentToken;
		var admin = await executor.Can(PortalPermission.FeedAdmin);
		var kinds = new List<SharpFeedKind>();
		foreach (var kind in await Mediator.Send(new GetFeedKindsQuery(), ct))
		{
			if (admin || await Feeds(parser).CanRunAsync(executor, kind)) kinds.Add(kind);
		}

		if (kinds.Count == 0)
			return MarkupText.Plain(admin
				? "No feed kinds. Define one with @feed/define <kind>=<owner>."
				: "You run no feed kinds.");

		var taps = await Mediator.Send(new GetFeedTapsQuery(null), ct);
		var rows = new List<string[]>();
		foreach (var kind in kinds)
		{
			var feeds = await Mediator.Send(new GetFeedsQuery(kind.Name), ct);
			rows.Add(
			[
				kind.Name,
				await DisplayAsync(kind.Owner),
				feeds.Count.ToString(CultureInfo.InvariantCulture),
				taps.Count(tap => tap.Kind == kind.Name).ToString(CultureInfo.InvariantCulture),
				kind.Description
			]);
		}

		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Kind")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Owner")) { Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("Feeds")) { Alignment = Alignment.Right, Wrap = false, Priority = 3 },
				new TableColumn(MarkupText.Plain("Taps")) { Alignment = Alignment.Right, Wrap = false, Priority = 3 },
				new TableColumn(MarkupText.Plain("Description")) { Min = 10 },
			],
			rows);
		var everyTap = taps.Count(tap => tap.Kind == FeedNames.Every);
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Feed kinds"), table,
			new TextBlock(MarkupText.Plain(everyTap == 0 ? "No taps on every kind." : $"{Counted(everyTap, "tap")} on every kind (*)."))), 78);
	}

	private async ValueTask<MString> FeedInfoAsync(IMUSHCodeParser parser, AnySharpObject executor, string text)
	{
		var ct = ExecutionBudget.CurrentToken;
		if (!FeedNames.TryParse(text, out var kindName, out var key))
			return MarkupText.Plain($"'{text}' is not a feed kind or feed. See help @feed.");
		if (await Feeds(parser).GetKindAsync(kindName) is not SharpFeedKind kind)
			return MarkupText.Plain($"No feed kind named '{kindName}'. See @feed/list.");
		if (!await Feeds(parser).CanRunAsync(executor, kind))
			return MarkupText.Plain($"Running feed kind '{kind.Name}' needs control of its owner or the {PortalPermission.FeedAdmin} permission.");

		var effective = kind.Effective;
		if (key is null)
		{
			var feeds = await Mediator.Send(new GetFeedsQuery(kind.Name), ct);
			var taps = await Mediator.Send(new GetFeedTapsQuery(kind.Name), ct);
			(string, MString)[] kindFields =
			[
				("Owner", MarkupText.Plain(await DisplayAsync(kind.Owner))),
				("Description", MarkupText.Plain(kind.Description.Length == 0 ? "none" : kind.Description)),
				.. SettingRows(kind.Settings, FeedSettings.Defaults, "default"),
				("Locks", MarkupText.Plain(LockList(kind.Locks))),
				("Feeds", MarkupText.Plain(feeds.Count.ToString(CultureInfo.InvariantCulture))),
				("Taps", MarkupText.Plain(taps.Count == 0 ? "none" : string.Join(", ", taps.Select(tap => $"{tap.Object}/{tap.Attribute}")))),
			];
			return ServerLayout.Build(ServerLayout.Section(MarkupText.Plain($"Feed kind {kind.Name}"), ServerLayout.KeyValues(kindFields)), 78);
		}

		if (await Mediator.Send(new GetFeedQuery(kind.Name, key), ct) is not SharpFeed feed)
			return MarkupText.Plain($"Feed {kind.Name}/{key} has no members or lines.");
		var members = await Mediator.Send(new GetFeedMembersQuery(kind.Name, key), ct);
		(string, MString)[] fields =
		[
			("Lines", MarkupText.Plain($"{feed.Messages.ToString(CultureInfo.InvariantCulture)}, {feed.Bytes.ToString(CultureInfo.InvariantCulture)} bytes")),
			("Newest", MarkupText.Plain(feed.LastId == 0 ? "none" : feed.LastId.ToString(CultureInfo.InvariantCulture))),
			("Members", MarkupText.Plain(members.Count.ToString(CultureInfo.InvariantCulture))),
			.. SettingRows(feed.Settings, effective, "kind"),
			("Locks", MarkupText.Plain(LockList(feed.Locks))),
		];
		return ServerLayout.Build(ServerLayout.Section(MarkupText.Plain($"Feed {feed.Name}"), ServerLayout.KeyValues(fields)), 78);
	}

	private static string LockList(IReadOnlyDictionary<string, string> locks)
		=> locks.Count == 0 ? "none" : string.Join("; ", locks.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}: {pair.Value}"));

	/// <summary>One row per option: its value, and where it comes from when it is not set here.</summary>
	private static IEnumerable<(string, MString)> SettingRows(FeedSettings set, FeedSettings fallback, string from)
	{
		var effective = set.Over(fallback);
		string Source(object? own) => own is null ? $" ({from})" : "";
		yield return ("max_messages", MarkupText.Plain(Limit(effective.MaxMessages) + Source(set.MaxMessages)));
		yield return ("max_bytes", MarkupText.Plain(Limit(effective.MaxBytes) + Source(set.MaxBytes)));
		yield return ("max_length", MarkupText.Plain(Limit(effective.MaxLength) + Source(set.MaxLength)));
		yield return ("max_age", MarkupText.Plain((effective.MaxAge is { } age && age > TimeSpan.Zero ? FormatAge(age) : "none") + Source(set.MaxAge)));
		yield return ("logged", MarkupText.Plain((effective.Logged == false ? "no" : "yes") + Source(set.Logged)));
		yield return ("style", MarkupText.Plain((effective.Style ?? FeedStyles.Say) + Source(set.Style)));
	}

	private static string Limit(long? value) => value is > 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : "none";

	private static string FormatAge(TimeSpan age)
		=> (long)age.TotalSeconds switch
		{
			var seconds and > 0 when seconds % 86400 == 0 => $"{seconds / 86400}d",
			var seconds and > 0 when seconds % 3600 == 0 => $"{seconds / 3600}h",
			var seconds and > 0 when seconds % 60 == 0 => $"{seconds / 60}m",
			var seconds => $"{seconds}s"
		};

	private async ValueTask<MString> FeedWhoAsync(FeedTarget target)
	{
		var members = await Mediator.Send(new GetFeedMembersQuery(target.Kind.Name, target.Feed.Key), ExecutionBudget.CurrentToken);
		if (members.Count == 0) return MarkupText.Plain($"Feed {target.Feed.Name} has no members.");

		var rows = new List<string[]>();
		foreach (var member in members)
		{
			rows.Add(
			[
				await DisplayAsync(member.Member),
				member.Gag ? "gag" : "-",
				member.JoinedAt.ToString(CultureInfo.InvariantCulture),
				member.LastSeen.ToString(CultureInfo.InvariantCulture)
			]);
		}

		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Member")) { Min = 10 },
				new TableColumn(MarkupText.Plain("Status")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Joined at")) { Alignment = Alignment.Right, Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("Seen to")) { Alignment = Alignment.Right, Wrap = false, Priority = 2 },
			],
			rows);
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain($"Members of {target.Feed.Name}"), table), 78);
	}

	/// <summary>
	/// <c>@feed/send</c>: the line comes from the enactor (the player a system speaks for), who must pass the send
	/// locks unless they may run the kind themselves. A leading <c>:</c> or <c>;</c> poses unless a style switch
	/// is given.
	/// </summary>
	private async ValueTask<string> FeedSendAsync(IMUSHCodeParser parser, AnySharpObject executor, FeedTarget target,
		MString right, string? style, bool hasTo, bool hasAs)
	{
		var to = new List<DBRef>();
		var text = right;
		if (hasTo)
		{
			var plain = text.ToPlainText();
			var slash = plain.IndexOf('/');
			if (slash < 0) return "Usage: @feed/send/to <kind>/<key>=<objids>/<message>.";
			foreach (var word in plain[..slash].Split(' ', StringSplitOptions.RemoveEmptyEntries))
			{
				if (!DBRef.TryParse(word, out var dbref) || dbref is not { } parsed)
					return $"'{word}' is not a dbref or objid.";
				to.Add(parsed);
			}

			text = text.Substring(slash + 1);
		}

		var displayName = "";
		if (hasAs)
		{
			var plain = text.ToPlainText();
			var slash = plain.IndexOf('/');
			if (slash < 0) return "Usage: @feed/send/as <kind>/<key>=<name>/<message>.";
			displayName = plain[..slash].Trim();
			text = text.Substring(slash + 1);
		}

		if (style is null)
		{
			style = text.ToPlainText() switch
			{
				[':', ..] => FeedStyles.Pose,
				[';', ..] => FeedStyles.SemiPose,
				_ => null
			};
			if (style is not null) text = text.Substring(1);
		}

		if (text.ToPlainText().Trim().Length == 0) return "Send what?";

		var speaker = await parser.CurrentState.KnownEnactorObject(Mediator);
		var feeds = Feeds(parser);
		if (!await feeds.CanRunAsync(speaker, target.Kind) && !await feeds.PassesAsync(target.Kind, target.Feed, FeedLocks.Send, speaker))
			return $"{Display(speaker)} may not send to {target.Feed.Name}.";

		var send = new FeedSend(target.Kind, target.Feed, speaker, executor,
			style ?? target.Feed.Settings.Over(target.Kind.Effective).Style ?? FeedStyles.Say, text, to, displayName);
		return await feeds.SendAsync(parser, send) switch
		{
			FeedDelivery => "",
			Error<string> error => error.Value
		};
	}

	private async ValueTask<string> FeedDeleteAsync(FeedTarget target)
		=> await Mediator.Send(new DeleteFeedCommand(target.Kind.Name, target.Feed.Key), ExecutionBudget.CurrentToken)
			? $"Deleted feed {target.Feed.Name}, its members and lines."
			: $"Feed {target.Feed.Name} has no members or lines.";

	/// <summary><c>/join</c>, <c>/leave</c>, the status switches and <c>/seen</c>: <c>@feed/&lt;op&gt; &lt;kind&gt;/&lt;key&gt;=&lt;player&gt;</c>.</summary>
	private async ValueTask<string> FeedMemberChangeAsync(IMUSHCodeParser parser, AnySharpObject executor,
		string operation, FeedTarget target, string right)
	{
		var ct = ExecutionBudget.CurrentToken;
		var (kind, feed) = target;
		var name = right;
		long? seenTo = null;
		if (operation == "SEEN" && right.LastIndexOf('/') is var slash and >= 0)
		{
			if (!long.TryParse(right[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
				return $"'{right[(slash + 1)..]}' is not a line id.";
			name = right[..slash];
			seenTo = id;
		}

		return await FeedObjectAsync(parser, executor, name) switch
		{
			AnySharpObject who => await FeedMemberChangeAsync(parser, operation, target, who, seenTo),
			Error<string> error => error.Value
		};
	}

	private async ValueTask<string> FeedMemberChangeAsync(IMUSHCodeParser parser, string operation, FeedTarget target,
		AnySharpObject who, long? seenTo)
	{
		var ct = ExecutionBudget.CurrentToken;
		var (kind, feed) = target;
		var dbref = who.Object().DBRef;
		var members = await Mediator.Send(new GetFeedMembersQuery(kind.Name, feed.Key), ct);
		var member = members.FirstOrDefault(m => m.Member == dbref);
		if (operation == "JOIN")
		{
			if (member is not null) return $"{Display(who)} is already on {feed.Name}.";
			if (!await Feeds(parser).PassesAsync(kind, feed, FeedLocks.Read, who))
				return $"{Display(who)} does not pass the read lock of {feed.Name}.";
			await Mediator.Send(new SetFeedMemberCommand(kind.Name, feed.Key,
				new SharpFeedMember(dbref, feed.LastId, false, feed.LastId)), ct);
			return $"Joined {Display(who)} to {feed.Name}.";
		}

		if (member is null) return $"{Display(who)} is not on {feed.Name}.";
		if (operation == "LEAVE")
		{
			await Mediator.Send(new RemoveFeedMemberCommand(kind.Name, feed.Key, dbref), ct);
			return $"Removed {Display(who)} from {feed.Name}.";
		}

		var changed = operation switch
		{
			"GAG" => member with { Gag = true },
			"UNGAG" => member with { Gag = false },
			_ => member with { LastSeen = seenTo ?? feed.LastId }
		};
		await Mediator.Send(new SetFeedMemberCommand(kind.Name, feed.Key, changed), ct);
		return operation switch
		{
			"SEEN" => $"{Display(who)} has seen {feed.Name} up to {changed.LastSeen.ToString(CultureInfo.InvariantCulture)}.",
			"GAG" => $"{Display(who)} is now gagged on {feed.Name}.",
			_ => $"{Display(who)} is no longer gagged on {feed.Name}."
		};
	}

	/// <summary>The <c>feed.admin</c> operations on kinds: defining, describing and tapping them.</summary>
	private async ValueTask<string> FeedKindChangeAsync(IMUSHCodeParser parser, AnySharpObject executor,
		string operation, string left, string right)
	{
		if (!await executor.Can(PortalPermission.FeedAdmin))
			return $"@feed/{operation.ToLowerInvariant()} needs the {PortalPermission.FeedAdmin} permission.";

		var ct = ExecutionBudget.CurrentToken;
		var feeds = Feeds(parser);
		switch (operation)
		{
			case "DEFINE":
				{
					var kindName = left.ToLowerInvariant();
					if (!FeedNames.IsKind(kindName))
						return $"'{left}' is not a feed kind name: a word in lower case, starting with a letter.";
					return await FeedObjectAsync(parser, executor, right) switch
					{
						AnySharpObject owner => await DefineKindAsync(kindName, owner),
						Error<string> error => error.Value
					};
				}
			case "UNDEFINE":
				return await Mediator.Send(new DeleteFeedKindCommand(left.ToLowerInvariant()), ct)
					? $"Removed feed kind {left.ToLowerInvariant()}, its feeds, their lines and its taps."
					: $"No feed kind named '{left}'.";
			case "DESCRIBE":
				return await feeds.GetKindAsync(left.ToLowerInvariant()) switch
				{
					SharpFeedKind kind => await DescribeKindAsync(kind with { Description = right }),
					Error<string> error => error.Value
				};
			case "TAP":
			case "UNTAP":
				return await FeedTapAsync(parser, executor, operation, left, right);
			default:
				return "Choose one @feed operation.";
		}

		async ValueTask<string> DefineKindAsync(string kindName, AnySharpObject owner)
		{
			var existing = await feeds.GetKindAsync(kindName);
			var kind = existing is SharpFeedKind found
				? found with { Owner = owner.Object().DBRef }
				: new SharpFeedKind(kindName, owner.Object().DBRef, "", FeedSettings.None, FeedLocks.None);
			await Mediator.Send(new SetFeedKindCommand(kind), ct);
			return existing is SharpFeedKind
				? $"Feed kind {kindName} is now owned by {Display(owner)}."
				: $"Defined feed kind {kindName}, owned by {Display(owner)}.";
		}

		async ValueTask<string> DescribeKindAsync(SharpFeedKind kind)
		{
			await Mediator.Send(new SetFeedKindCommand(kind), ct);
			return $"Described feed kind {kind.Name}.";
		}
	}

	/// <summary>
	/// <c>@feed/set &lt;target&gt;/&lt;option&gt;=&lt;value&gt;</c>, <c>/lock</c> and <c>/unlock</c>: on a kind with
	/// <c>feed.admin</c>, on one feed by whatever may run the kind.
	/// </summary>
	private async ValueTask<string> FeedOptionAsync(IMUSHCodeParser parser, AnySharpObject executor, string operation,
		string left, string right)
	{
		var ct = ExecutionBudget.CurrentToken;
		if (!FeedNames.TryParseOption(left, out var kindName, out var key, out var option))
			return $"Usage: @feed/{operation.ToLowerInvariant()} <kind>[/<key>]/<{(operation == "SET" ? "option" : "lock")}>{(operation == "UNLOCK" ? "" : "=<value>")}.";
		if (await Feeds(parser).GetKindAsync(kindName) is not SharpFeedKind kind)
			return $"No feed kind named '{kindName}'. See @feed/list.";
		if (key is null && !await executor.Can(PortalPermission.FeedAdmin))
			return $"@feed/{operation.ToLowerInvariant()} on a kind needs the {PortalPermission.FeedAdmin} permission.";
		if (key is not null && !await Feeds(parser).CanRunAsync(executor, kind))
			return $"Running feed kind '{kind.Name}' needs control of its owner or the {PortalPermission.FeedAdmin} permission.";

		var feed = key is null
			? null
			: await Mediator.Send(new GetFeedQuery(kindName, key), ct) switch
			{
				SharpFeed found => found,
				_ => SharpFeed.New(kindName, key)
			};
		var target = key is null ? $"feed kind {kind.Name}" : $"feed {feed!.Name}";

		if (operation == "SET")
		{
			return SetFeedOption(feed?.Settings ?? kind.Settings, option, right) switch
			{
				FeedSettings settings => await SaveAsync(settings, null,
					$"Set {option} on {target} to {(right.Length == 0 ? (key is null ? "the default" : "the kind's") : right)}."),
				Error<string> error => error.Value
			};
		}

		if (!FeedLocks.IsName(option)) return $"A feed has two locks, read and send; '{option}' is not one.";
		var lockString = operation == "LOCK" ? right : "";
		if (lockString.Length > 0)
		{
			if (await Mediator.Send(new GetObjectNodeQuery(kind.Owner), ct) is not AnySharpObject owner)
				return $"The owner of feed kind '{kind.Name}' is gone. See @feed/define.";
			if (!Locks(parser).Validate(lockString, owner)) return $"'{lockString}' is not a valid lock.";
		}

		return await SaveAsync(null, lockString,
			lockString.Length == 0 ? $"Unlocked {option} on {target}." : $"Locked {option} on {target}.");

		async ValueTask<string> SaveAsync(FeedSettings? settings, string? lockString, string done)
		{
			if (feed is null)
				await Mediator.Send(new SetFeedKindCommand(kind with
				{
					Settings = settings ?? kind.Settings,
					Locks = lockString is null ? kind.Locks : FeedLocks.With(kind.Locks, option, lockString)
				}), ct);
			else
				await Mediator.Send(new SetFeedCommand(feed with
				{
					Settings = settings ?? feed.Settings,
					Locks = lockString is null ? feed.Locks : FeedLocks.With(feed.Locks, option, lockString)
				}), ct);
			return done;
		}
	}

	/// <summary>One option of <c>@feed/set</c>; an empty value unsets it.</summary>
	private static Result<FeedSettings> SetFeedOption(FeedSettings settings, string option, string value)
	{
		var unset = value.Length == 0;
		switch (option)
		{
			case "max_messages":
			case "max_length":
				if (unset) return option == "max_messages" ? settings with { MaxMessages = null } : settings with { MaxLength = null };
				if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
					return new Error<string>($"{option} takes a whole number, 0 for no limit.");
				return option == "max_messages" ? settings with { MaxMessages = count } : settings with { MaxLength = count };
			case "max_bytes":
				if (unset) return settings with { MaxBytes = null };
				return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes)
					? settings with { MaxBytes = bytes }
					: new Error<string>("max_bytes takes a whole number, 0 for no limit.");
			case "max_age":
				if (unset) return settings with { MaxAge = null };
				return DurationSetting.TryParse(value, MaxFeedAgeSeconds, out var age)
					? settings with { MaxAge = age }
					: new Error<string>("max_age takes a time such as 30d, 12h or 90m, or 0 for no limit.");
			case "logged":
				if (unset) return settings with { Logged = null };
				return value.ToLowerInvariant() switch
				{
					"yes" or "on" or "1" or "true" => settings with { Logged = true },
					"no" or "off" or "0" or "false" => settings with { Logged = false },
					_ => new Error<string>("logged takes yes or no.")
				};
			case "style":
				if (unset) return settings with { Style = null };
				return FeedStyles.All.Contains(value.ToLowerInvariant())
					? settings with { Style = value.ToLowerInvariant() }
					: new Error<string>($"style takes one of: {string.Join(", ", FeedStyles.All)}.");
			default:
				return new Error<string>($"'{option}' is not a feed option. Options: max_messages, max_bytes, max_length, max_age, logged, style.");
		}
	}

	/// <summary><c>@feed/tap &lt;kind&gt;=&lt;object&gt;/&lt;attribute&gt;</c>; the kind may be <c>*</c>.</summary>
	private async ValueTask<string> FeedTapAsync(IMUSHCodeParser parser, AnySharpObject executor, string operation,
		string left, string right)
	{
		var kindName = left.ToLowerInvariant();
		if (kindName != FeedNames.Every && await Feeds(parser).GetKindAsync(kindName) is not SharpFeedKind)
			return $"No feed kind named '{left}'. See @feed/list.";
		var slash = right.IndexOf('/');
		if (slash <= 0 || slash == right.Length - 1)
			return $"Usage: @feed/{operation.ToLowerInvariant()} <kind>=<object>/<attribute>.";

		var attribute = right[(slash + 1)..].Trim().ToUpperInvariant();
		if (attribute.Any(char.IsWhiteSpace)) return $"'{attribute}' is not an attribute name.";
		return await FeedObjectAsync(parser, executor, right[..slash].Trim()) switch
		{
			AnySharpObject obj => await FeedTapAsync(executor, operation, kindName, obj, attribute),
			Error<string> error => error.Value
		};
	}

	private async ValueTask<string> FeedTapAsync(AnySharpObject executor, string operation, string kindName,
		AnySharpObject obj, string attribute)
	{
		var tap = new SharpFeedTap(kindName, obj.Object().DBRef, attribute);
		var feeds = kindName == FeedNames.Every ? "every kind" : $"feed kind {kindName}";
		if (operation == "UNTAP")
			return await Mediator.Send(new RemoveFeedTapCommand(tap), ExecutionBudget.CurrentToken)
				? $"Removed the tap {Display(obj)}/{attribute} from {feeds}."
				: $"{Display(obj)}/{attribute} does not tap {feeds}.";

		if (!await PermissionService.Controls(executor, obj))
			return $"You don't control {Display(obj)}, so you can't tap with it.";
		await Mediator.Send(new AddFeedTapCommand(tap), ExecutionBudget.CurrentToken);
		return $"{Display(obj)}/{attribute} now taps {feeds}.";
	}

	/// <summary><c>@feed/purge &lt;kind&gt;/&lt;key&gt;[=&lt;age&gt;]</c>: every line, or those older than the age.</summary>
	private async ValueTask<string> FeedPurgeAsync(FeedTarget target, string right)
	{
		DateTimeOffset? before = null;
		if (right.Length > 0)
		{
			if (!DurationSetting.TryParse(right, MaxFeedAgeSeconds, out var age) || age <= TimeSpan.Zero)
				return "Give an age such as 30d, 12h or 90m, or nothing to purge every line.";
			before = DateTimeOffset.UtcNow - age;
		}

		var purged = await Mediator.Send(new PurgeFeedCommand(target.Kind.Name, target.Feed.Key, before), ExecutionBudget.CurrentToken);
		return $"Purged {Counted(purged, "line")} from {target.Feed.Name}.";
	}
}
