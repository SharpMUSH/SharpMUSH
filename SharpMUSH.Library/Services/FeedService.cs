using System.Collections.Concurrent;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public class FeedService(
	IMediator mediator,
	IAttributeService attributeService,
	IPermissionService permissionService,
	ILockService lockService,
	INotifyService notifyService,
	IChannelMessageIdSource ids,
	Lazy<ITaskScheduler> scheduler,
	ILogger<FeedService> logger) : IFeedService
{
	public async ValueTask<Result<SharpFeedKind>> GetKindAsync(string kind)
		=> await mediator.Send(new GetFeedKindQuery(kind), ExecutionBudget.CurrentToken) switch
		{
			SharpFeedKind found => found,
			_ => new Error<string>($"No feed kind named '{kind}'. See @feed/list.")
		};

	public async ValueTask<bool> CanRunAsync(AnySharpObject executor, SharpFeedKind kind)
		=> await executor.Can(PortalPermission.FeedAdmin)
			|| (await OwnerAsync(kind) is AnySharpObject owner && await permissionService.Controls(executor, owner));

	public async ValueTask<bool> PassesAsync(SharpFeedKind kind, SharpFeed feed, string lockType, AnySharpObject unlocker)
	{
		var (kindLock, feedLock) = lockType == "read" ? (kind.ReadLock, feed.ReadLock) : (kind.SendLock, feed.SendLock);
		if (kindLock.Length == 0 && feedLock.Length == 0) return true;

		// A lock is evaluated as if it were on the kind's owner; with no owner left, nothing passes it.
		if (await OwnerAsync(kind) is not AnySharpObject owner) return false;
		return (kindLock.Length == 0 || await lockService.Evaluate(kindLock, owner, unlocker))
			&& (feedLock.Length == 0 || await lockService.Evaluate(feedLock, owner, unlocker));
	}

	public async ValueTask<Result<FeedDelivery>> SendAsync(IMUSHCodeParser parser, FeedSend send)
	{
		var ct = ExecutionBudget.CurrentToken;
		var (kind, feed, speaker, executor, style, text, to) = send;
		var settings = feed.Settings.Over(kind.Effective);
		if (settings.MaxLength is > 0 and var maxLength && text.ToPlainText().Length > maxLength)
			return new Error<string>($"That is longer than {feed.Name} takes ({maxLength} characters).");
		if (await OwnerAsync(kind) is not AnySharpObject owner)
			return new Error<string>($"The owner of feed kind '{kind.Name}' is gone. See @feed/define.");

		var id = await ids.NextAsync(ct);
		var speakerObject = speaker.Object();
		var location = (await speaker.Where()).Object().DBRef;
		var message = new SharpFeedMessage(id, kind.Name, feed.Key, DateTimeOffset.UtcNow, speakerObject.DBRef,
			speakerObject.Name, executor.Object().DBRef, location, style, text);

		// Stored first, so feedmsg() answers for it inside ROUTE, DELIVER, FORMAT and the taps.
		var stored = settings.Logged == true;
		if (stored) await mediator.Send(new AppendFeedMessageCommand(message, settings), ct);

		var members = await mediator.Send(new GetFeedMembersQuery(kind.Name, feed.Key), ct);
		var audience = members.Where(member => !member.Gag).Select(member => member.Member).ToList();
		var recipients = await RouteAsync(parser, owner, message, audience, to);
		var delivered = await DeliverAsync(parser, owner, speaker, message, recipients);
		await TapAsync(message, delivered);
		return new FeedDelivery(id, delivered, stored);
	}

	private ValueTask<AnyOptionalSharpObject> OwnerAsync(SharpFeedKind kind)
		=> mediator.Send(new GetObjectNodeQuery(kind.Owner), ExecutionBudget.CurrentToken);

	private async ValueTask<SharpAttribute[]?> TieInAsync(AnySharpObject owner, string kind, string part)
		=> await attributeService.GetAttributeAsync(owner, owner, IFeedService.TieIn(kind, part),
			IAttributeService.AttributeMode.Execute) is SharpAttribute[] chain
			? chain
			: null;

	/// <summary>The members who are not gagged, or what the kind's <c>ROUTE</c> returns in their place.</summary>
	private async ValueTask<List<AnySharpObject>> RouteAsync(IMUSHCodeParser parser, AnySharpObject owner,
		SharpFeedMessage message, IReadOnlyList<DBRef> audience, IReadOnlyList<DBRef> to)
	{
		IEnumerable<DBRef> routed = audience;
		if (await TieInAsync(owner, message.Kind, "ROUTE") is not null)
		{
			var result = await attributeService.EvaluateAttributeFunctionAsync(parser, owner, owner,
				IFeedService.TieIn(message.Kind, "ROUTE"), Arguments(
					message.Id.ToString(), message.Key, message.Speaker.ToString(), message.Style, message.Text,
					Objids(audience), Objids(to)));
			routed = result.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(word => DBRef.TryParse(word, out var dbref) ? dbref : null)
				.OfType<DBRef>();
		}

		var recipients = new List<AnySharpObject>();
		foreach (var dbref in routed.DistinctBy(dbref => dbref.Number))
		{
			if (await mediator.Send(new GetObjectNodeQuery(dbref), ExecutionBudget.CurrentToken) is AnySharpObject recipient)
				recipients.Add(recipient);
		}

		return recipients;
	}

	/// <summary>
	/// <c>DELIVER</c> runs once, in place, as the owner with the speaker as enactor. Without it, each recipient
	/// who can hear the speaker gets <c>FORMAT</c>'s line (none when it comes out empty) or the default line.
	/// </summary>
	private async ValueTask<IReadOnlyList<DBRef>> DeliverAsync(IMUSHCodeParser parser, AnySharpObject owner,
		AnySharpObject speaker, SharpFeedMessage message, List<AnySharpObject> recipients)
	{
		var ownerRef = owner.Object().DBRef;
		if (await TieInAsync(owner, message.Kind, "DELIVER") is [.., var deliver])
		{
			var arguments = Arguments(message.Id.ToString(), message.Key,
				Objids(recipients.Select(r => r.Object().DBRef)), message.Speaker.ToString(), message.Style, message.Text);
			var registers = new ConcurrentStack<Dictionary<string, MString>>();
			registers.Push([]);
			await parser.With(state => state with
			{
				Executor = ownerRef,
				Enactor = speaker.Object().DBRef,
				Caller = state.Executor,
				Registers = registers,
				Arguments = arguments,
				EnvironmentRegisters = arguments,
				CurrentEvaluation = new DBAttribute(ownerRef, deliver.LongName.ToUpperInvariant())
			}, p => p.WithAttributeDebug(deliver, async debug => await debug.CommandListParseVisitor(deliver.Value)()));
			return recipients.Select(r => r.Object().DBRef).ToList();
		}

		var format = await TieInAsync(owner, message.Kind, "FORMAT") is not null;
		var type = message.Style switch
		{
			FeedStyles.Pose => INotifyService.NotificationType.Pose,
			FeedStyles.SemiPose => INotifyService.NotificationType.SemiPose,
			FeedStyles.Emit => INotifyService.NotificationType.Emit,
			FeedStyles.Announce => INotifyService.NotificationType.Announce,
			_ => INotifyService.NotificationType.Say
		};
		var delivered = new List<DBRef>();
		foreach (var recipient in recipients)
		{
			if (!await permissionService.CanInteract(speaker, recipient, IPermissionService.InteractType.Hear)) continue;

			var line = format
				? await attributeService.EvaluateAttributeFunctionAsync(parser, owner, owner,
					IFeedService.TieIn(message.Kind, "FORMAT"), Arguments(message.Text,
						recipient.Object().DBRef.ToString(), message.Speaker.ToString(), message.Style, message.Key))
				: DefaultLine(message);
			if (line.Length == 0) continue;

			await notifyService.Notify(recipient, line, speaker, type);
			delivered.Add(recipient.Object().DBRef);
		}

		return delivered;
	}

	/// <summary><c>&lt;radio/101.5&gt; Ann says, "Coming in."</c>, and the same shapes as say, pose and @emit for the other styles.</summary>
	public static MString DefaultLine(SharpFeedMessage message)
	{
		var prefix = $"<{message.Feed}> ";
		return message.Style switch
		{
			FeedStyles.Pose => MarkupText.Concat(MarkupText.Plain($"{prefix}{message.SpeakerName} "), message.Text),
			FeedStyles.SemiPose => MarkupText.Concat(MarkupText.Plain($"{prefix}{message.SpeakerName}"), message.Text),
			FeedStyles.Emit or FeedStyles.Announce => MarkupText.Concat(MarkupText.Plain(prefix), message.Text),
			_ => MarkupText.Concat(MarkupText.Concat(MarkupText.Plain($"{prefix}{message.SpeakerName} says, \""), message.Text),
				MarkupText.Plain("\""))
		};
	}

	/// <summary>
	/// Every tap on the kind and on <c>*</c>, queued as the tap's object with the speaker as enactor, so a slow
	/// tap never holds the line up. A tap whose object or attribute is gone is skipped.
	/// </summary>
	private async ValueTask TapAsync(SharpFeedMessage message, IReadOnlyList<DBRef> recipients)
	{
		var ct = ExecutionBudget.CurrentToken;
		var taps = (await mediator.Send(new GetFeedTapsQuery(message.Kind), ct))
			.Concat(await mediator.Send(new GetFeedTapsQuery(FeedNames.Every), ct));
		foreach (var tap in taps)
		{
			if (await mediator.Send(new GetObjectNodeQuery(tap.Object), ct) is not AnySharpObject tapObject
				|| await attributeService.GetAttributeAsync(tapObject, tapObject, tap.Attribute,
					IAttributeService.AttributeMode.Execute) is not SharpAttribute[] chain)
			{
				continue;
			}

			var arguments = Arguments(message.Id.ToString(), message.Feed, Objids(recipients), message.Speaker.ToString(),
				message.Style, message.Text);
			var state = ParserState.RootFor(tapObject.Object().DBRef) with
			{
				Enactor = message.Speaker,
				Arguments = arguments,
				EnvironmentRegisters = new(arguments),
				CurrentEvaluation = new DBAttribute(tapObject.Object().DBRef, tap.Attribute)
			};
			var admission = await QueueHold.AdmitAsync(scheduler.Value, MarkupText.Plain(chain.Last().Value.ToPlainText()), state);
			if (!admission.Accepted)
				logger.LogWarning("Feed tap {Object}/{Attribute} was not queued for {Feed}: {Reason}", tap.Object,
					tap.Attribute, message.Feed, admission.Reason);
		}
	}

	private static string Objids(IEnumerable<DBRef> dbrefs) => string.Join(' ', dbrefs);

	private static Dictionary<string, CallState> Arguments(params object[] values)
		=> values.Index().ToDictionary(x => x.Index.ToString(), x => x.Item switch
		{
			MString text => new CallState(text),
			var other => new CallState(other.ToString()!)
		});
}
