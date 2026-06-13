using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The plan phase of the package plan/apply model (decision 20.7): pure,
/// read-only changeset computation. Callers gather the inputs (registry
/// records and a live-state snapshot); the engine classifies every object and
/// attribute action, runs three-way merge logic against stored baselines,
/// checks dependencies/conflicts, and detects $command collisions.
/// </summary>
public interface IPackagePlanService
{
	/// <summary>Computes the changeset an apply would execute. Never touches storage.</summary>
	PackageChangeset ComputeChangeset(PackagePlanInputs inputs);
}
