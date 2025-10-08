namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// This wrapper exists to make it possible to Mock IOptions in tests.
/// </summary>
/// <typeparam name="T">An IOption type</typeparam>
public interface IOptionsWrapper<T>
{
	T CurrentValue { get; }
}