using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.ListenPattern;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;
using static SharpMUSH.Library.Services.Interfaces.INotifyService;
using SharpMUSH.Library.Utilities;
using System.Text.RegularExpressions;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Service for routing notifications to listening objects.
/// Handles @listen attributes, ^-listen patterns, and puppet relaying.
/// </summary>
public class ListenerRoutingService(
	IMediator mediator,
	IListenPatternMatcher patternMatcher,
	IPermissionService permissionService,
	ILockService lockService,
	IConnectionService connectionService,
	IServiceProvider serviceProvider,
	IMessageBus publishEndpoint,
	IRealityPolicy reality,
	Lazy<INotifyService> notifier) : IListenerRoutingService
{
	public ListenerRoutingService(IMediator mediator, IListenPatternMatcher patternMatcher,
		IPermissionService permissionService, ILockService lockService, IConnectionService connectionService,
		IServiceProvider serviceProvider, IMessageBus publishEndpoint, IRealityPolicy reality)
		: this(mediator, patternMatcher, permissionService, lockService, connectionService, serviceProvider,
			publishEndpoint, reality, new Lazy<INotifyService>(() => serviceProvider.GetRequiredService<INotifyService>()))
	{ }

	/// <summary>Retains the published constructor for legacy callers, with reality filtering disabled.</summary>
	public ListenerRoutingService(IMediator mediator, IListenPatternMatcher patternMatcher,
		IPermissionService permissionService, ILockService lockService, IConnectionService connectionService,
		IServiceProvider serviceProvider, IMessageBus publishEndpoint)
		: this(mediator, patternMatcher, permissionService, lockService, connectionService,
			serviceProvider, publishEndpoint, DisabledRealityPolicy.Instance)
	{ }

	private IAttributeService? _attributeService;
	private IAttributeService AttributeService => _attributeService ??= serviceProvider.GetRequiredService<IAttributeService>();
	/// <summary>
	/// Runs the listener pass for the object this notification is addressed to.
	/// </summary>
	/// <remarks>
	/// It used to run the pass over every object in the sender's room, ignoring
	/// <see cref="NotificationContext.Target"/> entirely. A room broadcast is one notification per
	/// occupant, so a room of N enumerated its own contents N times and weighed all N objects each
	/// time: quadratic in occupancy, with a lock evaluation behind every gate — and every listener
	/// fired N times for one <c>say</c>, since each pass queued its matches again.
	/// <para>
	/// Each of the three things this does is about the object that heard the message: its ^-patterns
	/// match what it heard, its LISTEN attribute is its own, and a puppet relays what it was told. So
	/// the addressee is the right and only subject, the broadcast still reaches every occupant through
	/// the notification addressed to each of them, and each of them is weighed exactly once.
	/// </para>
	/// </remarks>
	public async ValueTask ProcessNotificationAsync(
		NotificationContext context,
		SharpMessage message,
		AnySharpObject? sender,
		NotificationType type)
	{
		if (!ShouldProcessListeners(type))
			return;

		if (context.Location is null)
			return;

		if (context.Exclusions.Contains(context.Target))
			return;

		if (await mediator.Send(new GetObjectNodeQuery(context.Target)) is not AnySharpObject listener)
			return;

		// Only when there is no speaker at all, which NotifyService never does: it routes nothing
		// without one. The location stands in for the speaker, as it did when this walked the room —
		// including the part where a location that resolves to nothing ends the pass rather than
		// throwing its way out of it.
		AnySharpObject actualSender;
		if (sender is not null)
		{
			actualSender = sender;
		}
		else
		{
			if (await mediator.Send(new GetObjectNodeQuery(context.Location.Value)) is not AnySharpObject location)
			{
				return;
			}

			actualSender = location;
		}

		if (!await permissionService.CanInteract(actualSender, listener, IPermissionService.InteractType.Hear))
			return;

		var messageText = message switch
		{
			MString markupString => markupString,
			string str => MString.Plain(str)
		};
		var matchingText = MString.Concat(context.Prefix, messageText);

		var options = serviceProvider.GetService<IOptionsWrapper<SharpMUSHOptions>>()?.CurrentValue.Attribute;
		// Penn's NA_PROPAGATE speech path bypasses PLAYER_LISTEN; deliberate private output does not.
		if (context.Relay != NotificationRelay.NoRelay && !listener.IsExit
			&& (!IsPrivate(type) || !listener.IsPlayer || options?.PlayerListen != false))
		{
			await ProcessListenAttributeAsync(listener, matchingText, messageText, actualSender, context, type,
				!listener.IsPlayer || options?.PlayerAHear != false);
			await ProcessListenPatternsAsync(listener, matchingText, actualSender);
		}

		if (context.Relay != NotificationRelay.NoRelay || context.PuppetOk || IsPrivate(type))
			await ProcessPuppetRelayAsync(listener, matchingText, actualSender, type);
	}

	private async ValueTask ProcessListenAttributeAsync(
		AnySharpObject listener,
		MString message,
		MString rawBody,
		AnySharpObject speaker,
		NotificationContext context,
		NotificationType type,
		bool hearActions)
	{
		var listenAttr = await AttributeService.GetAttributeAsync(
			listener, listener, "LISTEN",
			IAttributeService.AttributeMode.Read,
			parent: false);

		if (listenAttr is not SharpAttribute[] listen)
			return;

		var listenPattern = listen.Last().Value.ToPlainText();

		var isRegex = listen.Last().IsRegexp();
		var options = listen.Last().IsCase() ? RegexOptions.None : RegexOptions.IgnoreCase;
		Regex regex;
		try { regex = isRegex ? SoftcodeRegex.Create(listenPattern, options) : SoftcodeRegex.Wildcard(listenPattern, options, caseSensitive: !options.HasFlag(RegexOptions.IgnoreCase)); }
		catch (ArgumentException) { return; }
		if (SoftcodeRegex.Match(regex, message.ToPlainText()) is not { Success: true } match)
			return;

		if (hearActions && await lockService.Evaluate(LockType.Listen, listener, speaker))
		{
			var isSelf = listener.Object().DBRef == speaker.Object().DBRef;
			var arguments = PatternArguments.Capture(regex, match, isRegex, message);
			await mediator.Send(new ExecuteListenPatternCommand(listener, speaker, isSelf ? "AMHEAR" : "AHEAR", arguments), ExecutionBudget.CurrentToken);
			await mediator.Send(new ExecuteListenPatternCommand(listener, speaker, "AAHEAR", arguments), ExecutionBudget.CurrentToken);
		}
		if (context.Location == listener.Object().DBRef || !listener.IsContainer) return;
		await using var contents = listener.AsContainer.Content(mediator)
			.Where(content => content.IsPlayer || content.IsThing).GetAsyncEnumerator(ExecutionBudget.CurrentToken);
		if (!await contents.MoveNextAsync()) return;
		using (LockEvaluationArguments.Enter(new Dictionary<string, MString> { ["0"] = message }))
			if (!await lockService.Evaluate(LockType.InFilter, listener, speaker)) return;
		var filter = await AttributeService.GetAttributeAsync(listener, listener, "INFILTER", IAttributeService.AttributeMode.Read, parent: true);
		if (filter is SharpAttribute[] { Length: > 0 } filters)
		{
			var attribute = filters.Last();
			var compatibility = serviceProvider.GetService<IOptionsWrapper<SharpMUSHOptions>>()?.CurrentValue.Compatibility;
			var numeric = new NumericEvaluation(compatibility?.TinyMath ?? false, compatibility?.NullEqualsZero ?? false);
			foreach (var pattern in IncomingFilterPatterns.Split(attribute.Value.ToPlainText(), ExecutionBudget.CurrentToken))
				if (IncomingFilterPatterns.Matches(pattern, message.ToPlainText(), attribute.IsRegexp(), attribute.IsCase(), numeric)) return;
		}

		var prefix = MString.Empty;
		var prefixAttribute = await AttributeService.GetAttributeAsync(listener, listener, "INPREFIX", IAttributeService.AttributeMode.Read, parent: true);
		if (prefixAttribute is SharpAttribute[] { Length: > 0 })
		{
			var parser = serviceProvider.GetRequiredService<IMUSHCodeParser>();
			var state = parser.State.IsEmpty ? ParserState.RootFor(listener.Object().DBRef) : parser.CurrentState;
			var arguments = new Dictionary<string, CallState> { ["0"] = new(rawBody) };
			var scoped = parser.Push(state with
			{
				Executor = listener.Object().DBRef,
				Enactor = speaker.Object().DBRef,
				Caller = speaker.Object().DBRef,
				Arguments = arguments,
				EnvironmentRegisters = new(arguments),
				Registers = new(state.Registers.Reverse().Select(frame => new Dictionary<string, MString>(frame, frame.Comparer))),
				Restrictions = EvaluationRestrictions.Current ?? state.Restrictions
			});
			var result = await AttributeService.EvaluateAttributeFunctionResultAsync(scoped, listener, listener,
				"INPREFIX", arguments, ignorePermissions: true);
			ExecutionBudget.Current?.ThrowIfExceeded();
			if (result.HadErrors) return;
			prefix = MString.Concat(result.Message ?? MString.Empty, MString.Plain(" "));
		}
		var executor = speaker;
		if (context.Executor is DBRef reference)
		{
			if (await mediator.Send(new GetObjectNodeQuery(reference), ExecutionBudget.CurrentToken) is not AnySharpObject original) return;
			executor = original;
		}
		var relay = context with
		{
			Prefix = prefix,
			PuppetOk = true,
			Relay = context.Relay == NotificationRelay.Initial ? NotificationRelay.RelayOnce : NotificationRelay.NoRelay
		};
		do
		{
			var target = contents.Current.WithRoomOption();
			if (relay.Exclusions.Contains(target.Object().DBRef)) continue;
			if (!await permissionService.CanInteract(executor, target, IPermissionService.InteractType.Hear, speaker)) continue;
			await notifier.Value.NotifyWithContextAsync(relay with { Target = target.Object().DBRef }, rawBody, speaker, type);
		}
		while (await contents.MoveNextAsync());
	}

	private async ValueTask ProcessListenPatternsAsync(
		AnySharpObject listener,
		MString message,
		AnySharpObject speaker)
	{
		var hasMonitor = await listener.Object().Flags.Value.AnyAsync(f => f.Name == "MONITOR");
		if (!hasMonitor || await listener.HasFlag("HALT"))
			return;

		// Both locks have to pass, so a failing Use lock settles it — and a lock evaluation is now a
		// chain of awaited reads, not a field test, so the second one is worth not asking for.
		if (!await lockService.Evaluate(LockType.Use, listener, speaker)
				|| !await lockService.Evaluate(LockType.Listen, listener, speaker))
			return;

		var matches = await patternMatcher.MatchListenPatternsAsync(listener, message, speaker,
			checkParents: await listener.HasFlag("LISTEN_PARENT"));

		foreach (var match in matches)
		{
			var registers = match.Arguments.Count > 0 ? match.Arguments
				: match.CapturedGroups.Index().ToDictionary(pair => pair.Index.ToString(), pair => new CallState(pair.Item));
			var prefix = CommandDiscoveryService.ListenPatternRegex().Match(match.Attribute.Value.ToPlainText());
			if (!prefix.Success) continue;
			await mediator.Send(new ExecuteListenPatternCommand(listener, speaker, match.Attribute.LongName, registers)
			{
				Action = match.Attribute.Value.Substring(prefix.Length)
			}, ExecutionBudget.CurrentToken);
		}
	}

	private async ValueTask ProcessPuppetRelayAsync(
		AnySharpObject puppet,
		SharpMessage message,
		AnySharpObject speaker,
		NotificationType type)
	{
		var hasPuppet = await puppet.Object().Flags.Value.AnyAsync(f => f.Name == "PUPPET", ExecutionBudget.CurrentToken);
		if (!hasPuppet)
			return;

		var owner = await puppet.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken);
		if (owner is null) return;
		if (!await reality.CanPerceiveAsync(owner.Object.DBRef, speaker.Object().DBRef, ExecutionBudget.CurrentToken)
			|| !await reality.CanPerceiveAsync(owner.Object.DBRef, puppet.Object().DBRef, ExecutionBudget.CurrentToken)) return;

		// Snapshot immutable binding values before the remaining awaited relay reads. Metadata is
		// mutable, so retaining ConnectionData itself would not retain its original session.
		var bindings = await connectionService.Get(owner.Object.DBRef)
			.Select(connection => (connection.Handle, connection.Ref,
				Session: connection.Metadata.GetValueOrDefault("SessionId")))
			.ToArrayAsync(ExecutionBudget.CurrentToken);
		if (bindings.Length == 0) return;

		// Check if puppet and owner are in same location (unless VERBOSE)
		var hasVerbose = await puppet.Object().Flags.Value.AnyAsync(f => f.Name == "VERBOSE", ExecutionBudget.CurrentToken);
		if (!hasVerbose && !IsPrivate(type))
		{
			var puppetLocation = await LocateService.FriendlyWhereIs(puppet, ExecutionBudget.CurrentToken);
			var ownerLocation = await owner.Location.WithCancellation(ExecutionBudget.CurrentToken);

			if (puppetLocation.Object().DBRef == ownerLocation.Object().DBRef)
				return;
		}

		var prefixAttr = await AttributeService.GetAttributeAsync(
			puppet, puppet, "PREFIX",
			IAttributeService.AttributeMode.Read,
			parent: false);

		var prefix = prefixAttr is SharpAttribute[] prefixChain
			? prefixChain.Last().Value.ToPlainText()
			: $"{puppet.Object().Name}> ";

		// The relay stays an MString all the way to the ConnectionServer, which owns the wire format
		// — the same contract NotifyService publishes under. This used to render ANSI here and push
		// the bytes out as a TelnetOutputMessage, which skipped MarkupOutputRenderer entirely: on a
		// Pueblo or MXP connection the relayed text arrived neither entity-encoded nor line-mode
		// prefixed, so a '<' or '&' in what the puppet heard reached the client's parser raw. An
		// unprefixed line sits in MXP's default open mode, where <b>, <color> and <font> are honoured
		// — which made a puppet a route for one player's text to format another player's screen.
		var body = message switch
		{
			MString markupString => markupString,
			string str => MarkupText.Plain(str)
		};
		var header = body.Length == 0 ? MarkupText.Empty : await NameFormatter.HeaderAsync(owner, speaker, type,
			serviceProvider.GetService<IOptionsWrapper<SharpMUSHOptions>>()?.CurrentValue.Command.FullInvisibility ?? false, puppet);
		var relayed = MarkupText.Concat([MarkupText.Plain(prefix), header, body]);

		var serialized = MarkupTextSerializer.Serialize(relayed);
		foreach (var binding in bindings)
		{
			var current = connectionService.Get(binding.Handle);
			if (current is null || current.State != IConnectionService.ConnectionState.LoggedIn
				|| !Nullable.Equals(binding.Ref, owner.Object.DBRef)
				|| !Nullable.Equals(current.Ref, binding.Ref)
				|| !string.Equals(binding.Session, current.Metadata.GetValueOrDefault("SessionId"), StringComparison.Ordinal))
				continue;
			var output = new MarkupOutputMessage(binding.Handle, serialized) { SessionId = binding.Session };
			// Relayed text takes its place among the notifier's other output to this handle. Looked up
			// rather than forced, since the published constructors allow a provider with no notifier.
			if (serviceProvider.GetService<INotifyService>() is IOrderedHandlePublisher ordered)
				await ordered.Lane.PublishAsync(binding.Handle, binding.Session, token => publishEndpoint.HandlePublish(output, token), ExecutionBudget.CurrentToken);
			else
				await publishEndpoint.HandlePublish(output, ExecutionBudget.CurrentToken);
		}
	}

	private static bool ShouldProcessListeners(NotificationType type)
	{
		return type switch
		{
			NotificationType.Say => true,
			NotificationType.Pose => true,
			NotificationType.SemiPose => true,
			NotificationType.Emit => true,
			NotificationType.NSEmit => true,
			NotificationType.NSSay => true,
			NotificationType.NSPose => true,
			NotificationType.NSSemiPose => true,
			NotificationType.PrivateEmit => true,
			NotificationType.NSPrivateEmit => true,
			NotificationType.Announce => false,
			NotificationType.NSAnnounce => false,
			_ => false
		};
	}

	private static bool IsPrivate(NotificationType type) => type is NotificationType.PrivateEmit or NotificationType.NSPrivateEmit;
}
