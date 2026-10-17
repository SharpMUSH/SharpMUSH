using System.Security.Cryptography;
using System.Text.Json;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Server.Services;

public sealed record PackageBatchReviewSnapshot(CapabilityActor Actor, PackageBatchPlanRequest Request,
	PackageBatchPlanResponse Plan, string Fingerprint);
public sealed record PackageBatchAdmission(PackageBatchRunResponse Run, PackageBatchReviewSnapshot Snapshot, bool IsNew);

/// <summary>Process-local, bounded review handles and actor-owned run progress.</summary>
public sealed class PackageBatchRunRegistry
{
	private readonly object _gate = new();
	private readonly TimeProvider _time;
	private readonly int _capacity;
	private readonly TimeSpan _reviewLifetime;
	private readonly TimeSpan _runLifetime;
	private readonly long _retainedBytesLimit;
	private readonly Dictionary<string, ReviewEntry> _reviews = new(StringComparer.Ordinal);
	private readonly Dictionary<string, RunEntry> _runs = new(StringComparer.Ordinal);
	private long _retainedBytes;

	public PackageBatchRunRegistry(TimeProvider? time = null, int capacity = 64,
		TimeSpan? reviewLifetime = null, TimeSpan? runLifetime = null, long retainedBytesLimit = 32 * 1024 * 1024)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retainedBytesLimit);
		_time = time ?? TimeProvider.System;
		_capacity = Math.Min(capacity, 64);
		_reviewLifetime = reviewLifetime ?? TimeSpan.FromMinutes(15);
		_runLifetime = runLifetime ?? TimeSpan.FromHours(1);
		_retainedBytesLimit = Math.Min(retainedBytesLimit, 32 * 1024 * 1024);
	}

	public Result<PackageBatchPlanResponse> CreateReview(CapabilityActor actor, PackageBatchPlanRequest request,
		PackageBatchPlanResponse plan, string fingerprint)
	{
		if (request.Intents.Count > 32 || request.Reviews?.Count > 32 || plan.Steps.Count > 32)
			return new Error<string>("A batch may contain at most 32 intents and reviews.");
		// Count before allocating the retained copy, including escaped JSON strings.
		var snapshot = new PackageBatchReviewSnapshot(actor, request, plan with { ReviewToken = null, ExpiresAt = null }, fingerprint);
		using var counter = new BoundedStream(4 * 1024 * 1024);
		try { JsonSerializer.Serialize(counter, snapshot); }
		catch (ReviewTooLargeException) { return new Error<string>("Review exceeds the size limit."); }
		lock (_gate)
		{
			Cleanup();
			if (_reviews.Count >= 256 || counter.Length > _retainedBytesLimit - _retainedBytes)
				return new Error<string>("Review capacity is full; retry later.");
			var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);
			var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
			var expires = _time.GetUtcNow() + _reviewLifetime;
			_reviews.Add(token, new(actor, bytes, fingerprint, expires));
			_retainedBytes += bytes.Length;
			return ReadSnapshot(bytes).Plan with { ReviewToken = token, ExpiresAt = expires };
		}
	}

	public Result<PackageBatchAdmission> Admit(string token, CapabilityActor actor)
	{
		lock (_gate)
		{
			Cleanup();
			if (!_reviews.TryGetValue(token, out var review) || review.Actor != actor)
				return new Error<string>("Review is unknown or expired; request a fresh review.");
			if (review.RunId is { } existing && _runs.TryGetValue(existing, out var previous))
				return Admission(review, previous, false);
			if (_runs.Count >= _capacity)
				return new Error<string>("Run capacity is full; retry later.");
			var snapshot = ReadSnapshot(review.Bytes!);
			var id = Guid.NewGuid().ToString("N");
			var run = new RunEntry(new(id, PackageBatchRunState.Pending,
				snapshot.Plan.Steps.Select(x => new PackageBatchStepProgress(x.StepId, x.Intent, PackageBatchStepState.Pending)).ToArray()), review);
			run.SerializedBytes = Size(run.Response);
			if (run.SerializedBytes > _retainedBytesLimit - _retainedBytes)
				return new Error<string>("Run capacity is full; retry later.");
			_retainedBytes += run.SerializedBytes;
			_runs.Add(id, run);
			review.RunId = id;
			return Admission(review, run, true);
		}
	}

	public Found<PackageBatchRunResponse> Get(string id) => GetCore(id, null);
	public Found<PackageBatchRunResponse> Get(string id, CapabilityActor actor) => GetCore(id, actor);

	private Found<PackageBatchRunResponse> GetCore(string id, CapabilityActor? actor)
	{
		lock (_gate)
		{
			Cleanup();
			return _runs.TryGetValue(id, out var run) && (actor is null || run.Review.Actor == actor)
				? Copy(run.Response) : new NotFound();
		}
	}

	public void Update(string id, PackageBatchRunState state, IReadOnlyList<PackageBatchStepProgress> steps, string? message = null)
	{
		lock (_gate)
		{
			Cleanup();
			if (!_runs.TryGetValue(id, out var run) || run.TerminalAt is not null) return;
			var active = state is PackageBatchRunState.Pending or PackageBatchRunState.Running;
			var available = _retainedBytesLimit - _retainedBytes + run.SerializedBytes
				+ (active ? 0 : run.Review.Bytes?.Length ?? 0);
			var valid = steps.Count <= 32 && steps.Select(x => x.StepId).Distinct(StringComparer.Ordinal).Count() == steps.Count
				&& steps.All(x => run.Intents.TryGetValue(x.StepId, out var intent) && intent == x.Intent);
			var response = new PackageBatchRunResponse(id, state, steps, message);
			long size;
			try { size = valid ? Size(response, Math.Min(64 * 1024, available)) : long.MaxValue; }
			catch (ReviewTooLargeException) { size = long.MaxValue; }
			if (size == long.MaxValue)
			{
				// A lost progress payload cannot assert success or end a still-active execution lease.
				response = new(id, active ? state : PackageBatchRunState.Unknown, [], "Progress unavailable; refresh actual state.");
				size = Size(response);
				if (size > available)
				{
					response = response with { Message = null };
					size = Size(response);
				}
			}
			run.Response = Copy(response);
			_retainedBytes += size - run.SerializedBytes;
			run.SerializedBytes = size;
			if (active) return;
			run.TerminalAt = _time.GetUtcNow();
			ReleaseSnapshot(run.Review);
		}
	}

	private PackageBatchAdmission Admission(ReviewEntry review, RunEntry run, bool isNew) => new(Copy(run.Response),
		review.Bytes is { } bytes ? ReadSnapshot(bytes) : new(review.Actor, new([]), new([], [], [], null, null), review.Fingerprint), isNew);
	private static long Size<T>(T value, long limit = long.MaxValue)
	{
		using var counter = new BoundedStream(limit);
		JsonSerializer.Serialize(counter, value);
		return counter.Length;
	}

	private static PackageBatchReviewSnapshot ReadSnapshot(byte[] bytes) => JsonSerializer.Deserialize<PackageBatchReviewSnapshot>(bytes)!;
	private static PackageBatchRunResponse Copy(PackageBatchRunResponse response) => JsonSerializer.Deserialize<PackageBatchRunResponse>(JsonSerializer.SerializeToUtf8Bytes(response))!;

	private void Cleanup()
	{
		var now = _time.GetUtcNow();
		foreach (var pair in _runs.Where(x => x.Value.TerminalAt is { } at && now - at >= _runLifetime).ToArray())
		{
			_runs.Remove(pair.Key);
			_retainedBytes -= pair.Value.SerializedBytes;
			var token = _reviews.First(x => ReferenceEquals(x.Value, pair.Value.Review)).Key;
			ReleaseSnapshot(pair.Value.Review);
			_reviews.Remove(token);
		}
		foreach (var pair in _reviews.Where(x => x.Value.RunId is null && x.Value.ExpiresAt <= now).ToArray())
		{
			ReleaseSnapshot(pair.Value);
			_reviews.Remove(pair.Key);
		}
	}

	private void ReleaseSnapshot(ReviewEntry review)
	{
		if (review.Bytes is not { } bytes) return;
		_retainedBytes -= bytes.Length;
		CryptographicOperations.ZeroMemory(bytes);
		review.Bytes = null;
	}

	private sealed class ReviewEntry(CapabilityActor actor, byte[] bytes, string fingerprint, DateTimeOffset expiresAt)
	{
		public CapabilityActor Actor { get; } = actor;
		public byte[]? Bytes { get; set; } = bytes;
		public string Fingerprint { get; } = fingerprint;
		public DateTimeOffset ExpiresAt { get; } = expiresAt;
		public string? RunId { get; set; }
	}
	private sealed class RunEntry(PackageBatchRunResponse response, ReviewEntry review)
	{
		public PackageBatchRunResponse Response { get; set; } = response;
		public ReviewEntry Review { get; } = review;
		public IReadOnlyDictionary<string, PackageBatchIntent> Intents { get; } = response.Steps.ToDictionary(x => x.StepId, x => x.Intent, StringComparer.Ordinal);
		public long SerializedBytes { get; set; }
		public DateTimeOffset? TerminalAt { get; set; }
	}
	private sealed class ReviewTooLargeException : Exception;
	private sealed class BoundedStream(long limit) : Stream
	{
		private long _length;
		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => _length;
		public override long Position { get => _length; set => throw new NotSupportedException(); }
		public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
		public override void Write(ReadOnlySpan<byte> buffer)
		{
			if (buffer.Length > limit - _length) throw new ReviewTooLargeException();
			_length += buffer.Length;
		}
		public override void Flush() { }
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
	}
}
