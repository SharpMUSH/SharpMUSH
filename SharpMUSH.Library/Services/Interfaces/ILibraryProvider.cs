namespace SharpMUSH.Library.Services.Interfaces;

public interface ILibraryProvider<T>
{
	LibraryService<string, T> Get();
}