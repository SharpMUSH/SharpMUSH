using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// The feed functions (<c>help feed functions</c>): read-only, and only for code that may run the feed's kind
/// (control of its owner, or <c>feed.admin</c>), as <c>@feed</c> is.
/// </summary>
public partial class Functions
{
	private const string NoSuchFeedKind = "#-1 NO SUCH FEED KIND";
	private const string InvalidFeed = "#-1 INVALID FEED";

	/// <summary>A feed named in a function call, with its kind; the feed is new when it has no row yet.</summary>
	private sealed record FeedArgument(SharpFeedKind Kind, SharpFeed Feed);

	private static IFeedService FeedRules(IMUSHCodeParser parser) => parser.ServiceProvider.GetRequiredService<IFeedService>();

	private static string FeedArg(IMUSHCodeParser parser, int index)
		=> parser.CurrentState.Arguments.GetValueOrDefault(index.ToString(CultureInfo.InvariantCulture))?.Message.ToPlainText().Trim() ?? "";

	/// <summary>A kind the executor may run, or the error a function returns in its place.</summary>
	private async ValueTask<Result<SharpFeedKind>> RunnableKindAsync(IMUSHCodeParser parser, string kindName)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await FeedRules(parser).GetKindAsync(kindName) switch
		{
			SharpFeedKind kind when await FeedRules(parser).CanRunAsync(executor, kind) => kind,
			SharpFeedKind => new Error<string>(ErrorMessages.Returns.PermissionDenied),
			_ => new Error<string>(NoSuchFeedKind)
		};
	}

	/// <summary><c>&lt;kind&gt;/&lt;key&gt;</c> the executor may read, or the error a function returns in its place.</summary>
	private async ValueTask<Result<FeedArgument>> RunnableFeedAsync(IMUSHCodeParser parser, string text)
	{
		if (!FeedNames.TryParse(text, out var kindName, out var key) || key is null)
			return new Error<string>(InvalidFeed);
		return await RunnableKindAsync(parser, kindName) switch
		{
			SharpFeedKind kind => new FeedArgument(kind, await Mediator.Send(new GetFeedQuery(kind.Name, key), ExecutionBudget.CurrentToken) switch
			{
				SharpFeed feed => feed,
				_ => SharpFeed.New(kind.Name, key)
			}),
			Error<string> error => error
		};
	}

	private static CallState Objids(IEnumerable<DBRef> dbrefs) => new(string.Join(' ', dbrefs));

	/// <summary>
	/// <c>feedmsg(&lt;id&gt;, &lt;factor&gt;)</c>: one fact of a stored line: <c>id</c>, <c>feed</c>, <c>kind</c>,
	/// <c>key</c>, <c>time</c>, <c>speaker</c>, <c>name</c>, <c>executor</c>, <c>location</c>, <c>style</c> or
	/// <c>text</c> (markup kept).
	/// </summary>
	[SharpFunction(Name = "feedmsg", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["id", "factor"])]
	public async ValueTask<CallState> FeedMessage(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!long.TryParse(FeedArg(parser, 0), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
			return new CallState("#-1 INVALID FEED LINE ID");
		if (await Mediator.Send(new GetFeedMessageQuery(id), ExecutionBudget.CurrentToken) is not SharpFeedMessage message)
			return new CallState("#-1 NO SUCH FEED LINE");
		if (await RunnableKindAsync(parser, message.Kind) is Error<string> error)
			return new CallState(error.Value);

		return FeedArg(parser, 1).ToLowerInvariant() switch
		{
			"id" => new CallState(message.Id),
			"feed" => new CallState(message.Feed),
			"kind" => new CallState(message.Kind),
			"key" => new CallState(message.Key),
			"time" => new CallState(message.At.ToUnixTimeSeconds()),
			"speaker" => new CallState(message.Speaker),
			"name" => new CallState(message.SpeakerName),
			"executor" => new CallState(message.Executor),
			"executor_name" => new CallState(message.ExecutorName),
			"location" => message.Location is { } location ? new CallState(location) : CallState.Empty,
			"location_name" => new CallState(message.LocationName),
			"style" => new CallState(message.Style),
			"text" => new CallState(message.Text),
			"display" => new CallState(message.DisplayName),
			_ => new CallState("#-1 NO SUCH FACTOR")
		};
	}

	/// <summary>
	/// <c>feedrecall(&lt;feed&gt;, &lt;count&gt;[, &lt;after id&gt;])</c>: the ids of a feed's newest stored lines,
	/// oldest first; 0 for every line kept. With an id, only lines after it.
	/// </summary>
	[SharpFunction(Name = "feedrecall", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["feed", "count", "after id"])]
	public async ValueTask<CallState> FeedRecall(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!int.TryParse(FeedArg(parser, 1), NumberStyles.None, CultureInfo.InvariantCulture, out var count))
			return new CallState(ErrorMessages.Returns.UInteger);
		var after = 0L;
		if (FeedArg(parser, 2) is { Length: > 0 } afterText
			&& !long.TryParse(afterText, NumberStyles.None, CultureInfo.InvariantCulture, out after))
			return new CallState(ErrorMessages.Returns.UInteger);

		return await RunnableFeedAsync(parser, FeedArg(parser, 0)) switch
		{
			FeedArgument target => new CallState(string.Join(' ',
				(await Mediator.Send(new GetFeedMessagesQuery(target.Kind.Name, target.Feed.Key, count, after), ExecutionBudget.CurrentToken))
				.Select(message => message.Id))),
			Error<string> error => new CallState(error.Value)
		};
	}

	/// <summary>
	/// <c>feeds([&lt;kind&gt;])</c>: the kinds the executor runs, or one kind's feeds as <c>&lt;kind&gt;/&lt;key&gt;</c>.
	/// </summary>
	[SharpFunction(Name = "feeds", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["kind"])]
	public async ValueTask<CallState> FeedList(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var ct = ExecutionBudget.CurrentToken;
		if (FeedArg(parser, 0) is { Length: > 0 } kindName)
		{
			return await RunnableKindAsync(parser, kindName.ToLowerInvariant()) switch
			{
				SharpFeedKind kind => new CallState(string.Join(' ', (await Mediator.Send(new GetFeedsQuery(kind.Name), ct)).Select(feed => feed.Name))),
				Error<string> error => new CallState(error.Value)
			};
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var kinds = new List<string>();
		foreach (var kind in await Mediator.Send(new GetFeedKindsQuery(), ct))
		{
			if (await FeedRules(parser).CanRunAsync(executor, kind)) kinds.Add(kind.Name);
		}

		return new CallState(string.Join(' ', kinds));
	}

	/// <summary>
	/// <c>feedinfo(&lt;kind or feed&gt;, &lt;option&gt;)</c>: a setting as it applies (<c>max_messages</c>,
	/// <c>max_bytes</c>, <c>max_length</c>, <c>max_age</c> in seconds, <c>logged</c>, <c>style</c>), a lock
	/// (<c>read</c>, <c>send</c>), or <c>owner</c> and <c>description</c> of a kind, <c>messages</c>,
	/// <c>bytes</c>, <c>stored</c>, <c>last</c> and <c>members</c> of a feed; <c>feeds</c>, <c>messages</c>,
	/// <c>bytes</c> and <c>stored</c> totalled over a kind.
	/// </summary>
	[SharpFunction(Name = "feedinfo", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["kind or feed", "option"])]
	public async ValueTask<CallState> FeedInfo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!FeedNames.TryParse(FeedArg(parser, 0), out var kindName, out var key))
			return new CallState(InvalidFeed);
		return await RunnableKindAsync(parser, kindName) switch
		{
			SharpFeedKind kind => await FeedInfoAsync(kind, key, FeedArg(parser, 1).ToLowerInvariant()),
			Error<string> error => new CallState(error.Value)
		};
	}

	private async ValueTask<CallState> FeedInfoAsync(SharpFeedKind kind, string? key, string option)
	{
		if (key is null)
			return option switch
			{
				"owner" => new CallState(kind.Owner),
				"description" => new CallState(kind.Description),
				"read" or "send" => new CallState(kind.Lock(option)),
				"feeds" or "messages" or "bytes" or "stored" => KindHeld(
					(await Mediator.Send(new GetFeedUsageQuery(), ExecutionBudget.CurrentToken)).FirstOrDefault(usage => usage.Kind == kind.Name)
						?? new SharpFeedUsage(kind.Name, 0, 0, 0, 0), option),
				_ => SettingValue(kind.Effective, option)
			};

		var feed = await Mediator.Send(new GetFeedQuery(kind.Name, key), ExecutionBudget.CurrentToken) switch
		{
			SharpFeed found => found,
			_ => SharpFeed.New(kind.Name, key)
		};
		return option switch
		{
			"messages" => new CallState(feed.Messages),
			"bytes" => new CallState(feed.Bytes),
			"stored" => new CallState(feed.StoredBytes),
			"last" => new CallState(feed.LastId),
			"members" => new CallState((await Mediator.Send(new GetFeedMembersQuery(kind.Name, key), ExecutionBudget.CurrentToken)).Count),
			"read" or "send" => new CallState(feed.Lock(option)),
			_ => SettingValue(feed.Settings.Over(kind.Effective), option)
		};
	}

	private static CallState KindHeld(SharpFeedUsage usage, string option) => option switch
	{
		"feeds" => new CallState(usage.Feeds),
		"messages" => new CallState(usage.Messages),
		"bytes" => new CallState(usage.Bytes),
		_ => new CallState(usage.StoredBytes)
	};

	private static CallState SettingValue(FeedSettings settings, string option) => option switch
	{
		"max_messages" => new CallState(settings.MaxMessages ?? 0),
		"max_bytes" => new CallState(settings.MaxBytes ?? 0),
		"max_length" => new CallState(settings.MaxLength ?? 0),
		"max_age" => new CallState((long)(settings.MaxAge ?? TimeSpan.Zero).TotalSeconds),
		"logged" => new CallState(settings.Logged != false),
		"style" => new CallState(settings.Style ?? FeedStyles.Say),
		_ => new CallState("#-1 NO SUCH FEED OPTION")
	};

	/// <summary>
	/// <c>feedwho(&lt;feed&gt;[, &lt;status&gt;])</c>: the objids of a feed's members, or of those with a status:
	/// <c>gag</c>, or <c>active</c> (not gagged).
	/// </summary>
	[SharpFunction(Name = "feedwho", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["feed", "status"])]
	public async ValueTask<CallState> FeedWho(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		Func<SharpFeedMember, bool>? filter = FeedArg(parser, 1).ToLowerInvariant() switch
		{
			"" or "all" => _ => true,
			"gag" => member => member.Gag,
			"active" => member => !member.Gag,
			_ => null
		};
		if (filter is null) return new CallState("#-1 NO SUCH FEED STATUS");

		return await RunnableFeedAsync(parser, FeedArg(parser, 0)) switch
		{
			FeedArgument target => Objids((await Mediator.Send(new GetFeedMembersQuery(target.Kind.Name, target.Feed.Key), ExecutionBudget.CurrentToken))
				.Where(filter).Select(member => member.Member)),
			Error<string> error => new CallState(error.Value)
		};
	}

	/// <summary>
	/// <c>feedmember(&lt;feed&gt;, &lt;object&gt;[, &lt;field&gt;])</c>: 1 when the object is a member, else 0; or one
	/// field of its membership: <c>joined_at</c>, <c>last_seen</c> or <c>gag</c> (empty
	/// for a non-member).
	/// </summary>
	[SharpFunction(Name = "feedmember", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["feed", "object", "field"])]
	public async ValueTask<CallState> FeedMember(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await RunnableFeedAsync(parser, FeedArg(parser, 0)) switch
		{
			FeedArgument target => await WithFeedMemberAsync(parser, target, member => FeedArg(parser, 2).ToLowerInvariant() switch
			{
				"" or "member" => new CallState(member is not null),
				_ when member is null => CallState.Empty,
				"joined_at" => new CallState(member.JoinedAt),
				"last_seen" => new CallState(member.LastSeen),
				"gag" => new CallState(member.Gag),
				_ => new CallState("#-1 NO SUCH FEED FIELD")
			}),
			Error<string> error => new CallState(error.Value)
		};

	/// <summary>
	/// <c>feedunread(&lt;feed&gt;, &lt;object&gt;)</c>: how many stored lines are newer than the member's
	/// <c>last_seen</c>; empty for a non-member.
	/// </summary>
	[SharpFunction(Name = "feedunread", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["feed", "object"])]
	public async ValueTask<CallState> FeedUnread(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await RunnableFeedAsync(parser, FeedArg(parser, 0)) switch
		{
			FeedArgument target => await WithFeedMemberAsync(parser, target, async member => member is null
				? CallState.Empty
				: new CallState((await Mediator.Send(new GetFeedMessagesQuery(target.Kind.Name, target.Feed.Key, 0, member.LastSeen),
					ExecutionBudget.CurrentToken)).Count)),
			Error<string> error => new CallState(error.Value)
		};

	/// <summary><c>feedsof(&lt;object&gt;[, &lt;kind&gt;])</c>: the feeds the object is a member of, of the kinds the executor runs.</summary>
	[SharpFunction(Name = "feedsof", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "kind"])]
	public async ValueTask<CallState> FeedsOf(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var kindName = FeedArg(parser, 1).ToLowerInvariant();
		if (kindName.Length > 0 && await RunnableKindAsync(parser, kindName) is Error<string> error)
			return new CallState(error.Value);

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, FeedArg(parser, 0),
			LocateFlags.All, async found =>
			{
				var ct = ExecutionBudget.CurrentToken;
				var runnable = new Dictionary<string, bool>();
				var feeds = new List<string>();
				foreach (var (kind, key) in await Mediator.Send(new GetMemberFeedsQuery(found.Object().DBRef, kindName.Length == 0 ? null : kindName), ct))
				{
					if (!runnable.TryGetValue(kind, out var runs))
						runnable[kind] = runs = await RunnableKindAsync(parser, kind) is SharpFeedKind;
					if (runs) feeds.Add($"{kind}/{key}");
				}

				return new CallState(string.Join(' ', feeds));
			});
	}

	private ValueTask<CallState> WithFeedMemberAsync(IMUSHCodeParser parser, FeedArgument target,
		Func<SharpFeedMember?, CallState> answer)
		=> WithFeedMemberAsync(parser, target, member => ValueTask.FromResult(answer(member)));

	private async ValueTask<CallState> WithFeedMemberAsync(IMUSHCodeParser parser, FeedArgument target,
		Func<SharpFeedMember?, ValueTask<CallState>> answer)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, FeedArg(parser, 1),
			LocateFlags.All, async found =>
			{
				var members = await Mediator.Send(new GetFeedMembersQuery(target.Kind.Name, target.Feed.Key), ExecutionBudget.CurrentToken);
				return await answer(members.FirstOrDefault(member => member.Member == found.Object().DBRef));
			});
	}
}
