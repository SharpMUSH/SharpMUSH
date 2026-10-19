using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Semantics;

/// <summary>One allocation lifetime, from active admission through actual queued parent completion.</summary>
internal sealed class LegacyWikiPreparationScope : IDisposable
{
	internal NativeReadBudget Budget { get; }
	internal NativeReadReservation Lifetime { get; }
	internal SemanticFixtureCapability Owner { get; }
	private readonly CancellationTokenSource _cancellation;
	private bool _disposed;
	internal LegacyWikiPreparationScope(SemanticFixtureMutationLease admission, CancellationToken ct)
	{
		if (admission.Phase != SemanticFixturePhase.Active) throw SemanticFixtureCapability.Unavailable();
		Owner = admission.Owner;
		// Fixed token/budget bookkeeping belongs to the base intent overhead, before any retained input.
		_cancellation = ct.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(ct) : new CancellationTokenSource();
		try
		{
			Budget = new(1024, 128 * 1024, 4 * 1024 * 1024, _cancellation.Token, maxRecords: 128);
			Lifetime = Budget.ReserveWorking(1024);
		}
		catch { _cancellation.Dispose(); throw; }
	}
	internal CancellationTokenRegistration ObserveParentCancellation(CancellationToken ct)
	{
		RequireLive();
		Lifetime.ReserveAdditional(256);
		return ct.UnsafeRegister(static state => ((CancellationTokenSource)state!).Cancel(), _cancellation);
	}
	internal void RequireLive()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		Budget.CancellationToken.ThrowIfCancellationRequested();
	}
	internal string Retain(string value)
	{
		RequireLive();
		if (value is null) throw new WikiSemanticCapacityException("caller-string");
		Lifetime.ReserveAdditional(checked(32L + 2L * value.Length));
		return value;
	}
	internal ImmutableArray<string> Categories(IReadOnlyList<string> values)
	{
		RequireLive();
		if (values is null || values.Count > 128) throw new WikiSemanticCapacityException("categories");
		var count = values.Count;
		long strings = 0, binary = 4;
		for (var i = 0; i < count; i++)
		{
			Budget.CancellationToken.ThrowIfCancellationRequested();
			var value = values[i];
			if (string.IsNullOrWhiteSpace(value)) throw new WikiSemanticCapacityException("categories");
			strings = checked(strings + value.Length);
			binary = checked(binary + 4 + Encoding.UTF8.GetByteCount(value));
			if (binary > WikiSemanticLimits.MaxDescriptorBytes) throw new WikiSemanticCapacityException("descriptor");
		}
		Lifetime.ReserveAdditional(checked(64 + 16L * count + 2 * strings));
		Lifetime.ReserveAdditional(32L * count); // retained immutable string headers, not new normalization copies
		var copy = ImmutableArray.CreateBuilder<string>(count);
		long copiedStrings = 0, copiedBinary = 4;
		for (var i = 0; i < count; i++)
		{
			Budget.CancellationToken.ThrowIfCancellationRequested();
			var value = values[i];
			if (string.IsNullOrWhiteSpace(value)) throw new WikiSemanticCapacityException("categories");
			copiedStrings = checked(copiedStrings + value.Length);
			copiedBinary = checked(copiedBinary + 4 + Encoding.UTF8.GetByteCount(value));
			if (copiedStrings > strings || copiedBinary > binary) throw new WikiSemanticCapacityException("changing-categories");
			copy.Add(value);
		}
		return copy.MoveToImmutable();
	}
	internal string Target(string id)
	{
		Retain(id);
		var tail = id.StartsWith("wiki_page/", StringComparison.Ordinal) ? id.AsSpan(10) : id.AsSpan();
		if (tail.Contains('/') || !long.TryParse(tail, System.Globalization.NumberStyles.None,
			System.Globalization.CultureInfo.InvariantCulture, out var key)) return id;
		Lifetime.ReserveAdditional(256);
		return Retain("wiki_page/" + key.ToString(System.Globalization.CultureInfo.InvariantCulture));
	}
	internal string Digest(WikiLegacyMutationKind kind, IEnumerable<string?> fields)
	{
		RequireLive();
		Lifetime.ReserveAdditional(512);
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		Span<byte> frame = stackalloc byte[8];
		System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(frame, 1);
		System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(frame[4..], (int)kind);
		hash.AppendData(frame);
		foreach (var value in fields)
		{
			Budget.CancellationToken.ThrowIfCancellationRequested();
			Lifetime.ReserveAdditional(checked(32 + 3L * (value?.Length ?? 0)));
			System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(frame, value is null ? -1 : Encoding.UTF8.GetByteCount(value));
			hash.AppendData(frame);
			if (value is not null) hash.AppendData(Encoding.UTF8.GetBytes(value));
		}
		return Convert.ToHexStringLower(hash.GetHashAndReset());
	}
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		Lifetime.Dispose();
		_cancellation.Dispose();
	}
}
