namespace SharpMUSH.Library.Services.Interfaces;

public interface ILibraryProvider<T>
{
	LibraryService<string, T> Get();

	/// <summary>The definitions this provider was built with, before any softcode or plugin altered the library.</summary>
	IReadOnlyDictionary<string, T> Builtins { get; }
}