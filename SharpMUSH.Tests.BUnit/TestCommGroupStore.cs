using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Tests.BUnit;

/// <summary>An <see cref="ICommGroupStore"/> in memory: what was written, and a switch to refuse writes.</summary>
public sealed class TestCommGroupStore : ICommGroupStore
{
	public string? Json { get; set; }

	/// <summary>Writes fail while set, as a store that could not be reached.</summary>
	public bool Refuse { get; set; }

	public Task<Found<string>> ReadAsync() =>
		Task.FromResult<Found<string>>(Json is { } json ? json : new NotFound());

	/// <summary>While set, a write waits for it before it lands, as a store at the end of a slow network.</summary>
	public TaskCompletionSource? Hold { get; set; }

	public async Task<bool> WriteAsync(string json)
	{
		if (Hold is { } hold) await hold.Task;
		if (Refuse) return false;
		Json = json;
		return true;
	}

	/// <summary>Stands for another tab or device storing <paramref name="json"/>.</summary>
	public void ChangeElsewhere(string json)
	{
		Json = json;
		Changed?.Invoke();
	}

	public event Action? Changed;
}
