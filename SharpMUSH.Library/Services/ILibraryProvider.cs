namespace SharpMUSH.Library.Services;

public interface ILibraryProvider<T>
{
	LibraryService<string,T> Get();
}