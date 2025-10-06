namespace SharpMUSH.Library.Services.Interfaces;

public interface IOptionsWrapper<T>
{
	T CurrentValue { get; }
}