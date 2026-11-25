using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit;

/// <summary>An <see cref="ICommGroupStore"/> in memory: what was written, and a switch to refuse writes.</summary>
public sealed class TestCommGroupStore : ICommGroupStore
{
	public string? Json { get; set; }

	/// <summary>Writes fail while set, as a store that could not be reached.</summary>
	public bool Refuse { get; set; }

	public Task<string?> ReadAsync() => Task.FromResult(Json);

	public Task<bool> WriteAsync(string json)
	{
		if (Refuse) return Task.FromResult(false);
		Json = json;
		return Task.FromResult(true);
	}

	/// <summary>Stands for another tab or device storing <paramref name="json"/>.</summary>
	public void ChangeElsewhere(string json)
	{
		Json = json;
		Changed?.Invoke();
	}

	public event Action? Changed;
}
