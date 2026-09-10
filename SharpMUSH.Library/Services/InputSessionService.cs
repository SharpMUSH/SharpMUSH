using Mediator;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.InputSessions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>Owns connection capture generations; execution stays on the ordinary admitted scheduler.</summary>
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

	private sealed class Entry(InputSession session)
	{
		public InputSession Session { get; } = session;
		public bool TimeoutPending { get; set; }
	}

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
	private readonly IConnectionService _connections;
	private readonly IMediator _mediator;
	private readonly IAttributeService _attributes;
	private readonly IPermissionService _permissions;
	private readonly INotifyService _notify;
	private readonly TimeProvider _time;

	public InputSessionService(IConnectionService connections, IMediator mediator, IAttributeService attributes,
		IPermissionService permissions, INotifyService notify, TimeProvider? timeProvider = null)
	{
		_connections = connections;
		_mediator = mediator;
		_attributes = attributes;
		_permissions = permissions;
		_notify = notify;
		_time = timeProvider ?? TimeProvider.System;
		connections.ListenState(change =>
		{
			lock (_gate)
			{
				_sessions.Remove(change.Item1);
				foreach (var callback in _activeCallbacks)
					if (callback.Original.Connection.Handle == change.Item1) callback.Cancelled = true;
			}
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
		lock (_gate)
		{
			return _connections.Get(handle) is { } connection
				? new(GetCapturing(handle), GenerationFor(connection).Ticket) : default;
		}
	}

	public InputSession? GetCapturing(long handle)
	{
		lock (_gate)
		{
			if (!_sessions.TryGetValue(handle, out var entry)) return null;
			if (!BindingMatches(entry.Session)) { _sessions.Remove(handle); return null; }
			return entry.TimeoutPending || entry.Session.ExpiresAt <= _time.GetUtcNow() ? null : entry.Session;
		}
	}

	public async ValueTask<string?> StartAsync(IMUSHCodeParser parser, DBRef target, string attribute, MString prompt, TimeSpan timeout)
	{
		if (timeout < TimeSpan.FromSeconds(1) || timeout > TimeSpan.FromHours(1)) return InvalidTimeout;
		var state = parser.CurrentState;
		if (state.Handle is not { } handle || _connections.Get(handle) is not { Ref: { } character } connection
			|| connection.State != IConnectionService.ConnectionState.LoggedIn || state.Executor is not { } executor
			|| state.Enactor is not { } enactor || !TransportMatches(connection, state.ConnectionSessionId)) return InvalidContext;
		var player = await _mediator.Send(new GetObjectNodeQuery(character), ExecutionBudget.CurrentToken);
		var actor = await _mediator.Send(new GetObjectNodeQuery(executor), ExecutionBudget.CurrentToken);
		var source = await _mediator.Send(new GetObjectNodeQuery(target), ExecutionBudget.CurrentToken);
		var cause = await _mediator.Send(new GetObjectNodeQuery(enactor), ExecutionBudget.CurrentToken);
		if (player.IsNone || actor.IsNone || source.IsNone || cause.IsNone
			|| player.Known().Object().DBRef != cause.Known().Object().DBRef) return InvalidContext;
		if (await actor.Known().HasFlag("HALT", ExecutionBudget.CurrentToken) || !await CheckReadAsync(() => _permissions.Controls(actor.Known(), source.Known())))
			return ErrorMessages.Returns.PermissionDenied;
		if (!(await _attributes.GetAttributeAsync(actor.Known(), source.Known(), attribute, IAttributeService.AttributeMode.Read, false)).IsAttribute
			|| !(await _attributes.GetAttributeAsync(actor.Known(), source.Known(), attribute, IAttributeService.AttributeMode.Execute, false)).IsAttribute)
			return InvalidCallback;
		var owner = (await actor.Known().Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef;
		var callbackOwner = (await source.Known().Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef;
		var session = new InputSession(Guid.NewGuid(), connection, connection.Metadata.GetValueOrDefault("SessionId"),
			player.Known().Object().DBRef, actor.Known().Object().DBRef, owner, source.Known().Object().DBRef,
			callbackOwner, attribute, _time.GetUtcNow() + timeout);
		if (!session.Character.IsObjid || !session.Executor.IsObjid || !session.CallbackTarget.IsObjid
			|| !session.Owner.IsObjid || !session.CallbackOwner.IsObjid) return InvalidContext;
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
		}
		try { await _notify.PromptToSession(handle, session.TransportSessionId ?? "", prompt); }
		catch { Discard(session); throw; }
		return null;
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

	private static bool TransportMatches(IConnectionService.ConnectionData connection, string? expected)
		=> (connection.Metadata.GetValueOrDefault("SessionId") ?? "") == (expected ?? "");

	private async ValueTask<bool> CanManage(IMUSHCodeParser parser, InputSession session)
	{
		if (!TransportMatches(session.Connection, parser.CurrentState.ConnectionSessionId)
			|| parser.CurrentState.Executor is not { } executor) return false;
		var actor = await _mediator.Send(new GetObjectNodeQuery(executor), ExecutionBudget.CurrentToken);
		return !actor.IsNone && (actor.Known().Object().DBRef == session.Executor || actor.Known().Object().DBRef == session.Character);
	}

	public async ValueTask<string?> PromptAsync(IMUSHCodeParser parser, MString prompt)
	{
		if (parser.CurrentState.Handle is not { } handle || GetCapturing(handle) is not { } session) return NotActive;
		if (!await CanManage(parser, session)) return ErrorMessages.Returns.PermissionDenied;
		lock (_gate)
		{
			if (!IsCurrent(session, timeout: false) || HasCancelledOwnership(session)) return NotActive;
		}
		await _notify.PromptToSession(handle, session.TransportSessionId ?? "", prompt);
		return null;
	}

	public async ValueTask<string?> CancelAsync(IMUSHCodeParser parser)
	{
		if (parser.CurrentState.Handle is not { } handle || GetCapturing(handle) is not { } session) return NotActive;
		if (!await CanManage(parser, session)) return ErrorMessages.Returns.PermissionDenied;
		lock (_gate)
		{
			if (!IsCurrent(session, timeout: false) || HasCancelledOwnership(session)) return NotActive;
			CancelCallbacks(session);
			_sessions.Remove(handle);
		}
		await _notify.NotifyLocalizedToSession(handle, session.TransportSessionId ?? "", "InputSessionCancelled");
		return null;
	}

	public async ValueTask<bool> TryEscapeAsync(long handle, string? transportSessionId, MString input, Guid? expectedCapture = null)
	{
		if (!input.Text.Equals("@input/cancel", StringComparison.OrdinalIgnoreCase)) return false;
		InputSession session;
		lock (_gate)
		{
			if (!_sessions.TryGetValue(handle, out var entry) || entry.TimeoutPending
				|| entry.Session.ExpiresAt <= _time.GetUtcNow() || !BindingMatches(entry.Session)
				|| !TransportMatches(entry.Session.Connection, transportSessionId)) return false;
			if (expectedCapture is { } expected && entry.Session.Id != expected) return true;
			session = entry.Session;
			CancelCallbacks(session);
			_sessions.Remove(handle);
		}
		await _notify.NotifyLocalizedToSession(handle, session.TransportSessionId ?? "", "InputSessionCancelled");
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

	public IReadOnlyList<InputSession> TakeExpired()
	{
		lock (_gate)
		{
			var result = new List<InputSession>();
			foreach (var pair in _sessions.ToArray())
			{
				if (!BindingMatches(pair.Value.Session)) { _sessions.Remove(pair.Key); continue; }
				if (pair.Value.TimeoutPending || pair.Value.Session.ExpiresAt > _time.GetUtcNow()) continue;
				pair.Value.TimeoutPending = true;
				result.Add(pair.Value.Session);
			}
			return result;
		}
	}

	public void Discard(InputSession session)
	{
		lock (_gate)
			if (_sessions.TryGetValue(session.Connection.Handle, out var entry) && entry.Session.Id == session.Id)
				_sessions.Remove(session.Connection.Handle);
	}

	private bool IsCurrent(InputSession session, bool timeout)
	{
		lock (_gate)
			return _sessions.TryGetValue(session.Connection.Handle, out var entry) && entry.Session.Id == session.Id
				&& entry.TimeoutPending == timeout && BindingMatches(session)
				&& (timeout || session.ExpiresAt > _time.GetUtcNow());
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
			}
			finally { _callbackOwnership.Value = prior; }
		}
	}

	private async ValueTask<CallState?> DeliverCoreAsync(IMUSHCodeParser parser, InputSession session, MString input, bool timeout)
	{
		if (!IsCurrent(session, timeout)) return null;
		ExecutionBudget.Current?.ThrowIfExceeded();
		var actor = await _mediator.Send(new GetObjectNodeQuery(session.Executor), ExecutionBudget.CurrentToken);
		var target = await _mediator.Send(new GetObjectNodeQuery(session.CallbackTarget), ExecutionBudget.CurrentToken);
		var character = await _mediator.Send(new GetObjectNodeQuery(session.Character), ExecutionBudget.CurrentToken);
		if (actor.IsNone || target.IsNone || character.IsNone
			|| await actor.Known().HasFlag("HALT", ExecutionBudget.CurrentToken)
			|| (await actor.Known().Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef != session.Owner
			|| (await target.Known().Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef != session.CallbackOwner
			|| !await CheckReadAsync(() => _permissions.Controls(actor.Known(), target.Known()))) return await Revoke(session);
		var readable = await _attributes.GetAttributeAsync(actor.Known(), target.Known(), session.CallbackAttribute, IAttributeService.AttributeMode.Read, false);
		var executable = await _attributes.GetAttributeAsync(actor.Known(), target.Known(), session.CallbackAttribute, IAttributeService.AttributeMode.Execute, false);
		if (!readable.IsAttribute || !executable.IsAttribute) return await Revoke(session);
		if (!IsCurrent(session, timeout)) return null;
		ExecutionBudget.Current?.ThrowIfExceeded();
		if (input.Length > MaxInputCodeUnits)
		{
			await _notify.NotifyLocalizedToSession(session.Connection.Handle, session.TransportSessionId ?? "", "InputSessionInputTooLarge");
			return null;
		}
		if (timeout) Discard(session);
		var state = ParserState.RootFor(session.Executor) with
		{
			Executor = session.Executor,
			Caller = session.Executor,
			Enactor = session.Character,
			Handle = session.Connection.Handle,
			ConnectionSessionId = session.TransportSessionId,
			CurrentEvaluation = new DBAttribute(session.CallbackTarget, session.CallbackAttribute),
			EnvironmentRegisters = new Dictionary<string, CallState>
			{
				["0"] = new(input), ["1"] = new(timeout ? "timeout" : "input")
			}
		};
		return await parser.FromState(state).CommandListParse(executable.AsAttribute.Last().Value);
	}

	private async ValueTask<CallState?> Revoke(InputSession session)
	{
		Discard(session);
		if (BindingMatches(session)) await _notify.NotifyLocalizedToSession(session.Connection.Handle, session.TransportSessionId ?? "", "InputSessionRevoked");
		return null;
	}
}
