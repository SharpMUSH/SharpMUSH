using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Reports the world's storage capacity: map limit, file length, allocated disk and live data, what a
/// backup run needs, and what earlier imports left behind. Behind <c>@storage</c> and the
/// <c>sharpmush.storage.*</c> metrics.
/// </summary>
public interface IStorageCapacityService
{
	/// <summary>Measures it now. Cheap: environment statistics and a directory listing, no scan of the data.</summary>
	StorageCapacityReport Measure();
}
