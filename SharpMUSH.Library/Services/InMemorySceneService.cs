using System.Collections.Concurrent;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Scene;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// In-memory implementation of <see cref="ISceneService"/>.
/// Intended for unit tests and development use.
/// An ArangoDB-backed implementation will replace the persistence layer in a later phase.
/// </summary>
/// <remarks>
/// Thread-safe: all state is stored in <see cref="ConcurrentDictionary{TKey,TValue}"/> instances.
/// Message ordering is maintained by insertion order (Timestamp ascending).
/// </remarks>
public sealed class InMemorySceneService : ISceneService
{
	// ── Storage ───────────────────────────────────────────────────────────────

	private readonly ConcurrentDictionary<string, SceneArchive> _scenes = new();
	// sceneId → ordered (by Timestamp) list of messages
	private readonly ConcurrentDictionary<string, List<SceneMessage>> _messages = new();

	private int _sceneIdCounter;
	private int _messageIdCounter;

	// ── ISceneService: Archive / session management ───────────────────────────

	public Task<SceneArchive> OpenSceneAsync(
		string roomDbref,
		string roomName,
		string title = "",
		bool isPublic = true)
	{
		var id = NextSceneId();
		var scene = new SceneArchive(
			Id: id,
			Title: title,
			Description: string.Empty,
			RoomDbref: roomDbref,
			RoomName: roomName,
			ParticipantDbrefs: [],
			StartedAt: DateTimeOffset.UtcNow,
			ClosedAt: null,
			IsPublic: isPublic);

		_scenes[id] = scene;
		_messages[id] = [];
		return Task.FromResult(scene);
	}

	public Task<OneOf<SceneArchive, NotFound>> CloseSceneAsync(string sceneId)
	{
		if (!_scenes.TryGetValue(sceneId, out var scene))
			return Task.FromResult<OneOf<SceneArchive, NotFound>>(new NotFound());

		var closed = scene with { ClosedAt = DateTimeOffset.UtcNow };
		_scenes[sceneId] = closed;
		return Task.FromResult<OneOf<SceneArchive, NotFound>>(closed);
	}

	public Task<OneOf<SceneArchive, NotFound>> UpdateSceneMetaAsync(
		string sceneId,
		string? title = null,
		string? description = null)
	{
		if (!_scenes.TryGetValue(sceneId, out var scene))
			return Task.FromResult<OneOf<SceneArchive, NotFound>>(new NotFound());

		var updated = scene with
		{
			Title       = title       ?? scene.Title,
			Description = description ?? scene.Description,
		};
		_scenes[sceneId] = updated;
		return Task.FromResult<OneOf<SceneArchive, NotFound>>(updated);
	}

	public Task<OneOf<SceneArchive, NotFound>> GetSceneAsync(string sceneId)
	{
		if (_scenes.TryGetValue(sceneId, out var scene))
			return Task.FromResult<OneOf<SceneArchive, NotFound>>(scene);
		return Task.FromResult<OneOf<SceneArchive, NotFound>>(new NotFound());
	}

	public Task<IReadOnlyList<SceneArchive>> GetRecentScenesAsync(int count = 20)
	{
		IReadOnlyList<SceneArchive> result = _scenes.Values
			.Where(s => s.ClosedAt.HasValue)
			.OrderByDescending(s => s.ClosedAt)
			.Take(count)
			.ToList();
		return Task.FromResult(result);
	}

	public Task<IReadOnlyList<SceneArchive>> GetActiveScenesAsync()
	{
		IReadOnlyList<SceneArchive> result = _scenes.Values
			.Where(s => !s.ClosedAt.HasValue)
			.OrderByDescending(s => s.StartedAt)
			.ToList();
		return Task.FromResult(result);
	}

	// ── ISceneService: Message operations ────────────────────────────────────

	public Task<OneOf<SceneMessage, NotFound, Error<string>>> PostMessageAsync(
		string sceneId,
		string authorDbref,
		string authorName,
		string plainContent,
		string renderedHtml,
		SceneMessageType messageType = SceneMessageType.Pose)
	{
		if (!_scenes.TryGetValue(sceneId, out var scene))
			return Task.FromResult<OneOf<SceneMessage, NotFound, Error<string>>>(new NotFound());

		if (scene.ClosedAt.HasValue)
			return Task.FromResult<OneOf<SceneMessage, NotFound, Error<string>>>(
				new Error<string>($"Scene {sceneId} is closed and cannot accept new messages."));

		var msg = new SceneMessage(
			Id:           NextMessageId(),
			SceneId:      sceneId,
			AuthorDbref:  authorDbref,
			AuthorName:   authorName,
			Content:      plainContent,
			RenderedHtml: renderedHtml,
			Timestamp:    DateTimeOffset.UtcNow,
			MessageType:  messageType);

		var list = _messages.GetOrAdd(sceneId, _ => []);
		lock (list) list.Add(msg);

		// Update participant set on the scene record
		if (!scene.ParticipantDbrefs.Contains(authorDbref))
		{
			var updated = scene with
			{
				ParticipantDbrefs = [..scene.ParticipantDbrefs, authorDbref],
			};
			_scenes[sceneId] = updated;
		}

		return Task.FromResult<OneOf<SceneMessage, NotFound, Error<string>>>(msg);
	}

	public Task<OneOf<IReadOnlyList<SceneMessage>, NotFound>> GetMessagesAsync(string sceneId)
	{
		if (!_scenes.ContainsKey(sceneId))
			return Task.FromResult<OneOf<IReadOnlyList<SceneMessage>, NotFound>>(new NotFound());

		if (!_messages.TryGetValue(sceneId, out var list))
		{
			IReadOnlyList<SceneMessage> emptyList = Array.Empty<SceneMessage>();
			return Task.FromResult(OneOf<IReadOnlyList<SceneMessage>, NotFound>.FromT0(emptyList));
		}

		IReadOnlyList<SceneMessage> result;
		lock (list) result = list.OrderBy(m => m.Timestamp).ToList();
		return Task.FromResult(OneOf<IReadOnlyList<SceneMessage>, NotFound>.FromT0(result));
	}

	public Task<OneOf<IReadOnlyList<SceneMessage>, NotFound>> GetRecentMessagesAsync(
		string sceneId,
		int count = 50)
	{
		if (!_scenes.ContainsKey(sceneId))
			return Task.FromResult<OneOf<IReadOnlyList<SceneMessage>, NotFound>>(new NotFound());

		if (!_messages.TryGetValue(sceneId, out var list))
		{
			IReadOnlyList<SceneMessage> emptyList = Array.Empty<SceneMessage>();
			return Task.FromResult(OneOf<IReadOnlyList<SceneMessage>, NotFound>.FromT0(emptyList));
		}

		IReadOnlyList<SceneMessage> result;
		lock (list)
		{
			result = list.OrderBy(m => m.Timestamp).TakeLast(count).ToList();
		}
		return Task.FromResult(OneOf<IReadOnlyList<SceneMessage>, NotFound>.FromT0(result));
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private string NextSceneId()   => $"scene_{Interlocked.Increment(ref _sceneIdCounter)}";
	private string NextMessageId() => $"msg_{Interlocked.Increment(ref _messageIdCounter)}";
}
