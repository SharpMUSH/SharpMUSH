using Mediator;
using SharpMUSH.Configuration.Options;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.InputSessions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Library.Services;

/// <summary>Owns connection capture generations; execution stays on the ordinary admitted scheduler.</summary>
/// <remarks>
/// Every prompt and lifecycle notice takes its place in the handle's publication order while
/// <see cref="_gate"/> commits the transition it belongs to, and publishes after the lock is released.
/// A prompt authorized before a cancellation, replacement or rebind therefore reaches the connection
/// before anything published after that transition, when the notifier orders publications
/// (<see cref="IOrderedHandlePublisher"/>); any other notifier publishes as the calls arrive.
/// </remarks>
public sealed class InputSessionService : IInputSessionService
{
	public const int MaxSessions = 1024;
	public const int MaxOwnerSessions = 64;
	public const int MaxInputCodeUnits = 65_536;
	public const string InvalidContext = "#-1 INPUT SESSION REQUIRES A CURRENT CHARACTER CONNECTION";
	public const string InvalidCallback = "#-1 INVALID INPUT CALLBACK";
	public const string SessionLimit = "#-1 INPUT SESSION LIMIT EXCEEDED";
	public const string NotActive = "#-1 NO ACTIVE INPUT SESSION";
	public const string PendingTimeout = "#-1 INPUT SESSION TIMEOUT CALLBACK IS PENDING";
	public const string InvalidTimeout = "#-1 INPUT TIMEOUT MUST BE BETWEEN 1 AND 3600 SECONDS";
	public const string MissingExit = "#-1 INPUT NEEDS AN EXIT PATTERN AND ATTRIBUTE";
	public const string UnpairedPattern = "#-1 INPUT PATTERNS AND ATTRIBUTES COME IN PAIRS";
	public const string InvalidPattern = "#-1 INVALID INPUT PATTERN";
	public const string ReservedExit = "#-1 INPUT EXIT PATTERN CANNOT BE @INPUT/CANCEL";
	/// <summary>The line that always leaves a session without its callback; <see cref="TryEscapeAsync"/> takes it first.</summary>
	public const string CancelLine = "@input/cancel";
	/// <summary>The named argument, read with <c>r(reason,args)</c>, that says why an attribute runs: exit, input or timeout.</summary>
	public const string ReasonArgument = "reason";

	private sealed class Entry(InputSession session)
	{
		public InputSession Session { get; } = session;
		public bool TimeoutPending { get; set; }
	}

	/// <summary>A session that ended, and the place its clear takes in its handle's publication order.</summary>
	private readonly record struct Ended(InputSession Session, HandlePublicationLane.Slot? Place);

	private sealed class CallbackOwnership(InputSession original, CallbackOwnership? parent)
	{
		public InputSession Original { get; } = original;
		public CallbackOwnership? Parent { get; } = parent;
		public InputSession? Replacement { get; set; }
		public bool Closed { get; set; }
		public bool Cancelled { get; set; }
	}

	// Execution context identifies starts performed by this callback, not unrelated starts
	// on the same handle. Recording and closure are serialized with capture publication.
	private readonly AsyncLocal<CallbackOwnership?> _callbackOwnership = new();
	private readonly HashSet<CallbackOwnership> _activeCallbacks = [];

	private sealed class Generation { public InputCaptureTicket Ticket { get; set; } = new(Guid.NewGuid()); }
	// Metadata survives bind/unbind but belongs to one transport incarnation. Weak keys do not
	// retain disconnected handles, while their last capture still fences previously queued replies.
	private readonly ConditionalWeakTable<ConcurrentDictionary<string, string>, Generation> _generations = new();
	private readonly Lock _gate = new();
	private readonly Dictionary<long, Entry> _sessions = [];
	// Sessions End removed whose clear is not sent yet; guarded by _gate.
	private readonly List<Ended> _ended = [];
	private readonly IConnectionService _connections;
	private readonly IMediator _mediator;
	private readonly IAttributeService _attributes;
	private readonly IPermissionService _permissions;
	private readonly INotifyService _notify;
	private readonly TimeProvider _time;
	private readonly HandlePublicationLane? _lane;
	private readonly IOptionsWrapper<SharpMUSHOptions>? _configuration;

	public InputSessionService(IConnectionService connections, IMediator mediator, IAttributeService attributes,
		IPermissionService permissions, INotifyService notify, TimeProvider? timeProvider = null,
		IOptionsWrapper<SharpMUSHOptions>? configuration = null)
	{
		_configuration = configuration;
		_connections = connections;
		_mediator = mediator;
		_attributes = attributes;
		_permissions = permissions;
		_notify = notify;
		_time = timeProvider ?? TimeProvider.System;
		_lane = (notify as IOrderedHandlePublisher)?.Lane;
		connections.ListenState(change =>
		{
			lock (_gate)
			{
				End(change.Item1);
				foreach (var callback in _activeCallbacks)
					if (callback.Original.Connection.Handle == change.Item1) callback.Cancelled = true;
			}
			SendEnded();
		});
	}

	private bool BindingMatches(InputSession session)
	{
		var current = _connections.Get(session.Connection.Handle);
		return current is not null && ReferenceEquals(current, session.Connection)
			&& current.State == IConnectionService.ConnectionState.LoggedIn
			&& current.Metadata.GetValueOrDefault("SessionId") == session.TransportSessionId;
	}

	private Generation GenerationFor(IConnectionService.ConnectionData connection)
		=> _generations.GetValue(connection.Metadata, static _ => new Generation());

	public Guid GetCaptureGeneration(long handle)
	{
		lock (_gate) return _connections.Get(handle) is { } connection ? GenerationFor(connection).Ticket.InitialGeneration : Guid.Empty;
	}

	public InputCaptureSnapshot CapturePendingInput(long handle)
	{
		try
		{
			lock (_gate)
			{
				return _connections.Get(handle) is { } connection
					? new(GetCapturing(handle), GenerationFor(connection).Ticket) : default;
			}
		}
		finally { SendEnded(); }
	}

	public InputSession? GetCapturing(long handle)
	{
		try
		{
			lock (_gate)
			{
				if (!_sessions.TryGetValue(handle, out var entry)) return null;
				if (!BindingMatches(entry.Session)) { End(handle); return null; }
				return entry.TimeoutPending || entry.Session.ExpiresAt <= _time.GetUtcNow() ? null : entry.Session;
			}
		}
		finally { SendEnded(); }
	}

	/// <summary>
	/// The one way a session leaves <see cref="_sessions"/>. Called under <see cref="_gate"/>, it takes the
	/// place of the session's clear in the handle's publication order, so the clear follows everything the
	/// session published and precedes whatever comes after it; <see cref="SendEnded"/> sends it once the
	/// lock is released. A connection that is gone, or does not order prompts, is sent nothing.
	/// </summary>
	private void End(long handle)
	{
		if (!_sessions.Remove(handle, out var entry)) return;
		_ended.Add(new Ended(entry.Session, _lane?.Reserve(handle, entry.Session.TransportSessionId)));
	}

	/// <summary>Sends the clears <see cref="End"/> queued, unless the caller still holds <see cref="_gate"/>.</summary>
	private void SendEnded()
	{
		if (_gate.IsHeldByCurrentThread) return;
		Ended[] ended;
		lock (_gate)
		{
			if (_ended.Count == 0) return;
			ended = [.. _ended];
			_ended.Clear();
		}
		foreach (var session in ended) _ = ClearAsync(session);
	}

	private async Task ClearAsync(Ended ended)
	{
		var session = ended.Session;
		try
		{
			using (Publishing(ended.Place))
				await _notify.ClearPromptToSession(session.Connection.Handle, session.TransportSessionId ?? "", session.Id);
		}
		catch
		{
			// The session has ended either way; its prompt stays up until the next prompt or a resume replaces it.
		}
	}

	public async ValueTask<string?> StartAsync(IMUSHCodeParser parser, MString prompt, IReadOnlyList<InputRouteSpec> routes,
		InputMatch match, TimeSpan timeout)
	{
		if (routes.Count == 0 || routes.Any(route => string.IsNullOrWhiteSpace(route.Pattern) || string.IsNullOrWhiteSpace(route.Attribute)))
			return MissingExit;
		if (routes[0].Pattern.Trim().Equals(CancelLine, StringComparison.OrdinalIgnoreCase))
			return ReservedExit;
		if (timeout < TimeSpan.FromSeconds(1) || timeout > TimeSpan.FromHours(1)) return InvalidTimeout;
		var state = parser.CurrentState;
		if (state.Handle is not { } handle || _connections.Get(handle) is not { Ref: { } character } connection
			|| connection.State != IConnectionService.ConnectionState.LoggedIn || state.Executor is not { } executor
			|| state.Enactor is not { } enactor || !TransportMatches(connection, state.ConnectionSessionId)) return InvalidContext;
		if (await _mediator.Send(new GetObjectNodeQuery(character), ExecutionBudget.CurrentToken) is not AnySharpObject player
			|| await _mediator.Send(new GetObjectNodeQuery(executor), ExecutionBudget.CurrentToken) is not AnySharpObject actor
			|| await _mediator.Send(new GetObjectNodeQuery(enactor), ExecutionBudget.CurrentToken) is not AnySharpObject cause
			|| player.Object().DBRef != cause.Object().DBRef) return InvalidContext;
		if (await actor.HasFlag("HALT", ExecutionBudget.CurrentToken)) return ErrorMessages.Returns.PermissionDenied;
		var built = new List<InputRoute>(routes.Count);
		foreach (var spec in routes)
		{
			switch (await RouteAsync(actor, spec, match))
			{
				case InputRoute route: built.Add(route); break;
				case Error<string> error: return error.Value;
			}
		}
		var owner = (await actor.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef;
		var caller = state.Caller is { } calledBy
			&& await _mediator.Send(new GetObjectNodeQuery(calledBy), ExecutionBudget.CurrentToken) is AnySharpObject callerObject
				? callerObject.Object().DBRef
				: actor.Object().DBRef;
		HandlePublicationLane.Slot? place = null;
		var session = new InputSession(Guid.NewGuid(), connection, connection.Metadata.GetValueOrDefault("SessionId"),
			player.Object().DBRef, actor.Object().DBRef, owner, caller, built, _time.GetUtcNow() + timeout);
		if (!session.Character.IsObjid || !session.Executor.IsObjid || !session.Owner.IsObjid
			|| built.Any(route => !route.Target.IsObjid || !route.TargetOwner.IsObjid)) return InvalidContext;
		lock (_gate)
		{
			if (!BindingMatches(session)) return InvalidContext;
			if (HasCancelledOwnership(session)) return NotActive;
			if (_sessions.TryGetValue(handle, out var previous) && BindingMatches(previous.Session)
				&& (previous.TimeoutPending || previous.Session.ExpiresAt <= _time.GetUtcNow())) return PendingTimeout;
			if (!_sessions.ContainsKey(handle) && _sessions.Count >= MaxSessions) return SessionLimit;
			if (_sessions.Count(pair => pair.Key != handle && pair.Value.Session.Owner == owner) >= MaxOwnerSessions) return SessionLimit;
			_sessions[handle] = new Entry(session);
			for (var callback = _callbackOwnership.Value; callback is { Closed: false }; callback = callback.Parent)
			{
				if (ReferenceEquals(callback.Original.Connection, connection)
					&& callback.Original.TransportSessionId == session.TransportSessionId)
					callback.Replacement = session;
			}
			var generation = GenerationFor(connection);
			generation.Ticket.ObserveStart(session.Id);
			generation.Ticket = new InputCaptureTicket(session.Id);
			place = _lane?.Reserve(handle, session.TransportSessionId);
		}
		try
		{
			using (Publishing(place)) await _notify.PromptToSession(handle, session.TransportSessionId ?? "", prompt, session.Id);
		}
		catch { Discard(session); throw; }
		return null;
	}

	/// <summary>
	/// <paramref name="spec"/> made ready to match: its object controlled by <paramref name="actor"/>, its
	/// attribute readable and executable there, its pattern compiled. The error to give otherwise.
	/// </summary>
	private async ValueTask<Result<InputRoute>> RouteAsync(AnySharpObject actor, InputRouteSpec spec, InputMatch match)
	{
		if (await _mediator.Send(new GetObjectNodeQuery(spec.Target), ExecutionBudget.CurrentToken) is not AnySharpObject source)
			return new Error<string>(InvalidContext);
		if (!await CheckReadAsync(() => _permissions.Controls(actor, source))) return new Error<string>(ErrorMessages.Returns.PermissionDenied);
		var attribute = spec.Attribute.Trim();
		if (!(await _attributes.GetAttributeAsync(actor, source, attribute, IAttributeService.AttributeMode.Read, false)).IsAttribute
			|| !(await _attributes.GetAttributeAsync(actor, source, attribute, IAttributeService.AttributeMode.Execute, false)).IsAttribute)
			return new Error<string>(InvalidCallback);
		var pattern = spec.Pattern.Trim();
		Regex? matcher = null;
		if (pattern != "*" && match != InputMatch.Exact)
		{
			try
			{
				matcher = match == InputMatch.Regex
					? SoftcodeRegex.Create(pattern, RegexOptions.IgnoreCase)
					: SoftcodeRegex.Wildcard(pattern);
			}
			catch (ArgumentException) { return new Error<string>(InvalidPattern); }
		}
		var owner = (await source.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef;
		return new InputRoute(pattern, matcher, match == InputMatch.Regex, source.Object().DBRef, owner, attribute);
	}

	private static async ValueTask<bool> CheckReadAsync(Func<ValueTask<bool>> read)
	{
		var token = ExecutionBudget.CurrentToken;
		token.ThrowIfCancellationRequested();
		// Legacy permission APIs have no token parameter. Bound only their read-only
		// decision; callback execution and output publication remain directly awaited.
		var pending = read();
		var result = pending.IsCompletedSuccessfully ? pending.Result : await pending.AsTask().WaitAsync(token);
		token.ThrowIfCancellationRequested();
		return result;
	}

	/// <summary>Publishes the next notification to the slot's handle in the place reserved for it.</summary>
	private IDisposable? Publishing(HandlePublicationLane.Slot? place)
		=> place is null ? null : _lane!.Bind(place);

	private static bool TransportMatches(IConnectionService.ConnectionData connection, string? expected)
		=> (connection.Metadata.GetValueOrDefault("SessionId") ?? "") == (expected ?? "");

	private async ValueTask<bool> CanManage(IMUSHCodeParser parser, InputSession session)
	{
		if (!TransportMatches(session.Connection, parser.CurrentState.ConnectionSessionId)
			|| parser.CurrentState.Executor is not { } executor) return false;
		return await _mediator.Send(new GetObjectNodeQuery(executor), ExecutionBudget.CurrentToken) is AnySharpObject actor
			&& (actor.Object().DBRef == session.Executor || actor.Object().DBRef == session.Character);
	}

	public async ValueTask<string?> PromptAsync(IMUSHCodeParser parser, MString prompt)
	{
		if (parser.CurrentState.Handle is not { } handle || GetCapturing(handle) is not { } session) return NotActive;
		if (!await CanManage(parser, session)) return ErrorMessages.Returns.PermissionDenied;
		HandlePublicationLane.Slot? place;
		lock (_gate)
		{
			if (!IsCurrent(session, timeout: false) || HasCancelledOwnership(session)) return NotActive;
			place = _lane?.Reserve(handle, session.TransportSessionId);
		}
		using (Publishing(place)) await _notify.PromptToSession(handle, session.TransportSessionId ?? "", prompt, session.Id);
		return null;
	}

	public async ValueTask<string?> CancelAsync(IMUSHCodeParser parser)
	{
		if (parser.CurrentState.Handle is not { } handle || GetCapturing(handle) is not { } session) return NotActive;
		if (!await CanManage(parser, session)) return ErrorMessages.Returns.PermissionDenied;
		HandlePublicationLane.Slot? place;
		lock (_gate)
		{
			if (!IsCurrent(session, timeout: false) || HasCancelledOwnership(session)) return NotActive;
			CancelCallbacks(session);
			place = _lane?.Reserve(handle, session.TransportSessionId);
			End(handle);
		}
		SendEnded();
		using (Publishing(place)) await _notify.NotifyLocalizedToSession(handle, session.TransportSessionId ?? "", "InputSessionCancelled");
		return null;
	}

	public async ValueTask<bool> TryEscapeAsync(long handle, string? transportSessionId, MString input, Guid? expectedCapture = null)
	{
		if (!input.Text.Equals(CancelLine, StringComparison.OrdinalIgnoreCase)) return false;
		InputSession session;
		HandlePublicationLane.Slot? place;
		lock (_gate)
		{
			if (!_sessions.TryGetValue(handle, out var entry) || entry.TimeoutPending
				|| entry.Session.ExpiresAt <= _time.GetUtcNow() || !BindingMatches(entry.Session)
				|| !TransportMatches(entry.Session.Connection, transportSessionId)) return false;
			if (expectedCapture is { } expected && entry.Session.Id != expected) return true;
			session = entry.Session;
			CancelCallbacks(session);
			place = _lane?.Reserve(handle, session.TransportSessionId);
			End(handle);
		}
		SendEnded();
		using (Publishing(place)) await _notify.NotifyLocalizedToSession(handle, session.TransportSessionId ?? "", "InputSessionCancelled");
		return true;
	}

	// Read under _gate, just before an input operation commits or publishes.
	private bool HasCancelledOwnership(InputSession session)
	{
		for (var callback = _callbackOwnership.Value; callback is not null; callback = callback.Parent)
			if (callback.Cancelled && ReferenceEquals(callback.Original.Connection.Metadata, session.Connection.Metadata)
				&& callback.Original.TransportSessionId == session.TransportSessionId) return true;
		return false;
	}

	// Called under _gate after validating the current capture. Fence already-running
	// callbacks on this transport; future independent starts have no cancelled ownership.
	private void CancelCallbacks(InputSession session)
	{
		foreach (var callback in _activeCallbacks)
			if (ReferenceEquals(callback.Original.Connection, session.Connection)
				&& callback.Original.TransportSessionId == session.TransportSessionId)
				callback.Cancelled = true;
	}

	public async ValueTask<int> RescueAsync(DBRef character)
	{
		var rescued = new List<(InputSession Session, HandlePublicationLane.Slot? Place)>();
		lock (_gate)
		{
			var now = _time.GetUtcNow();
			foreach (var pair in _sessions.ToArray())
			{
				var session = pair.Value.Session;
				if (session.Character != character || pair.Value.TimeoutPending || session.ExpiresAt <= now
					|| !BindingMatches(session)) continue;
				// The timeout worker picks the session up on its next tick and runs the callback as it would
				// have at expiry, so the softcode's own timeout branch decides what happens to the work.
				_sessions[pair.Key] = new Entry(session with { ExpiresAt = now });
				rescued.Add((session, _lane?.Reserve(pair.Key, session.TransportSessionId)));
			}
		}
		foreach (var (session, place) in rescued)
			using (Publishing(place))
				await _notify.NotifyLocalizedToSession(session.Connection.Handle, session.TransportSessionId ?? "", "InputSessionRescued");
		return rescued.Count;
	}

	public IReadOnlyList<InputSession> TakeExpired()
	{
		try
		{
			lock (_gate)
			{
				var result = new List<InputSession>();
				foreach (var pair in _sessions.ToArray())
				{
					if (!BindingMatches(pair.Value.Session)) { End(pair.Key); continue; }
					if (pair.Value.TimeoutPending || pair.Value.Session.ExpiresAt > _time.GetUtcNow()) continue;
					pair.Value.TimeoutPending = true;
					result.Add(pair.Value.Session);
				}
				return result;
			}
		}
		finally { SendEnded(); }
	}

	public void Discard(InputSession session)
	{
		lock (_gate)
			if (_sessions.TryGetValue(session.Connection.Handle, out var entry) && entry.Session.Id == session.Id)
				End(session.Connection.Handle);
		SendEnded();
	}

	public async ValueTask ClearPromptUnlessCapturingAsync(long handle)
	{
		string sessionId;
		HandlePublicationLane.Slot? place;
		try
		{
			lock (_gate)
			{
				if (GetCapturing(handle) is not null
					|| _connections.Get(handle)?.Metadata.GetValueOrDefault("SessionId") is not { } current) return;
				sessionId = current;
				place = _lane?.Reserve(handle, sessionId);
			}
		}
		finally { SendEnded(); }
		using (Publishing(place)) await _notify.ClearPromptToSession(handle, sessionId, null);
	}

	private bool IsCurrent(InputSession session, bool timeout)
	{
		lock (_gate)
			return _sessions.TryGetValue(session.Connection.Handle, out var entry) && entry.Session.Id == session.Id
				&& entry.TimeoutPending == timeout && BindingMatches(session)
				&& (timeout || entry.Session.ExpiresAt > _time.GetUtcNow());
	}

	public async ValueTask<CallState?> DeliverAsync(IMUSHCodeParser parser, InputSession session, MString input, bool timeout = false)
	{
		var prior = _callbackOwnership.Value;
		var ownership = new CallbackOwnership(session, prior);
		_callbackOwnership.Value = ownership;
		lock (_gate) _activeCallbacks.Add(ownership);
		var failed = true;
		try
		{
			var result = await DeliverCoreAsync(parser, session, input, timeout);
			failed = result?.HadErrors == true;
			return result;
		}
		finally
		{
			try
			{
				lock (_gate)
				{
					ownership.Closed = true;
					_activeCallbacks.Remove(ownership);
					if (failed || ExecutionBudget.Current?.IsExceeded == true)
					{
						Discard(session);
						if (ownership.Replacement is { } replacement) Discard(replacement);
					}
				}
				SendEnded();
			}
			finally { _callbackOwnership.Value = prior; }
		}
	}

	private async ValueTask<CallState?> DeliverCoreAsync(IMUSHCodeParser parser, InputSession session, MString input, bool timeout)
	{
		if (!IsCurrent(session, timeout)) return null;
		ExecutionBudget.Current?.ThrowIfExceeded();
		// The timeout runs the exit route with no line; a line runs the first route it matches. A line too
		// long to match, or one nothing matches, still has its authority checked against the exit route.
		var oversized = !timeout && input.Length > MaxInputCodeUnits;
		var (matched, arguments) = timeout ? (session.Exit, []) : oversized ? (null, []) : Route(session, input);
		var route = matched ?? session.Exit;
		if (await _mediator.Send(new GetObjectNodeQuery(session.Executor), ExecutionBudget.CurrentToken) is not AnySharpObject actor
			|| await _mediator.Send(new GetObjectNodeQuery(route.Target), ExecutionBudget.CurrentToken) is not AnySharpObject target
			|| await _mediator.Send(new GetObjectNodeQuery(session.Character), ExecutionBudget.CurrentToken) is not AnySharpObject character
			|| await actor.HasFlag("HALT", ExecutionBudget.CurrentToken)
			|| (await actor.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef != session.Owner
			|| (await target.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef != route.TargetOwner
			|| !await CheckReadAsync(() => _permissions.Controls(actor, target))) return await Revoke(session);
		var readable = await _attributes.GetAttributeAsync(actor, target, route.Attribute, IAttributeService.AttributeMode.Read, false);
		var executable = await _attributes.GetAttributeAsync(actor, target, route.Attribute, IAttributeService.AttributeMode.Execute, false);
		if (readable is not SharpAttribute[] || executable is not SharpAttribute[] callback) return await Revoke(session);
		if (!IsCurrent(session, timeout)) return null;
		ExecutionBudget.Current?.ThrowIfExceeded();
		if (oversized)
		{
			await _notify.NotifyLocalizedToSession(session.Connection.Handle, session.TransportSessionId ?? "", "InputSessionInputTooLarge");
			return null;
		}
		if (matched is null)
		{
			await _notify.NotifyLocalizedToSession(session.Connection.Handle, session.TransportSessionId ?? "",
				"InputSessionNoMatchFormat", session.Exit.Pattern);
			return null;
		}
		var exit = ReferenceEquals(route, session.Exit);
		if (exit) Discard(session);
		// A named argument, as a regexp's named group is, so it lives only as long as this run; it
		// replaces a group of the same name.
		arguments[ReasonArgument] = new CallState(MString.Plain(timeout ? "timeout" : exit ? "exit" : "input"), 0);
		var state = ParserState.RootFor(session.Executor) with
		{
			Executor = session.Executor,
			Caller = session.Caller,
			Enactor = session.Character,
			Handle = session.Connection.Handle,
			ConnectionSessionId = session.TransportSessionId,
			CurrentEvaluation = new DBAttribute(route.Target, route.Attribute),
			OutputLimit = await FunctionLimits.OutputLimitForAsync(character,
				_configuration?.CurrentValue.Limit.GuestOutputLimit ?? LimitOptions.DefaultGuestOutputLimit),
			EnvironmentRegisters = arguments
		};

		return await parser.FromState(state).CommandListParse(callback.Last().Value);
	}

	/// <summary>
	/// The first of <paramref name="session"/>'s routes that <paramref name="input"/> matches, and what it
	/// passes on: a wildcard's or regexp's captures as $-commands number them, otherwise the line in %0.
	/// Null when none matches.
	/// </summary>
	private static (InputRoute? Route, Dictionary<string, CallState> Arguments) Route(InputSession session, MString input)
	{
		var line = input.Trim(TrimType.TrimBoth);
		var plain = line.ToPlainText();
		foreach (var route in session.Routes)
		{
			if (route.IsAny) return (route, new() { ["0"] = new(input) });
			if (route.Matcher is null)
			{
				if (plain.Equals(route.Pattern, StringComparison.OrdinalIgnoreCase)) return (route, new() { ["0"] = new(input) });
				continue;
			}
			if (SoftcodeRegex.Match(route.Matcher, plain) is { Success: true } match)
				return (route, PatternArguments.Capture(route.Matcher, match, route.IsRegex, line));
		}
		return (null, []);
	}

	private async ValueTask<CallState?> Revoke(InputSession session)
	{
		HandlePublicationLane.Slot? place;
		try
		{
			lock (_gate)
			{
				Discard(session);
				if (!BindingMatches(session)) return null;
				place = _lane?.Reserve(session.Connection.Handle, session.TransportSessionId);
			}
		}
		finally { SendEnded(); }
		using (Publishing(place)) await _notify.NotifyLocalizedToSession(session.Connection.Handle, session.TransportSessionId ?? "", "InputSessionRevoked");
		return null;
	}
}
