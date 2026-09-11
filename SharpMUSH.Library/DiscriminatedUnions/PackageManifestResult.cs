using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A parsed package document, or the issues that stopped it parsing.
/// </summary>
public union PackageManifestResult<T>(T, PackageManifestFailure);
