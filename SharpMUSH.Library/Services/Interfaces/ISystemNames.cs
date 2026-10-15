using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The system an object belongs to, as a context name: the package that created it, from the package
/// registry, or else the object's own objid. Running one of its <c>$</c>-commands pushes this name.
/// </summary>
public interface ISystemNames
{
	ValueTask<string> SystemOfAsync(DBRef obj, CancellationToken cancellationToken = default);
}
