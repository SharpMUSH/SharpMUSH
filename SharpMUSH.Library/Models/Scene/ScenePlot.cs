namespace SharpMUSH.Library.Models.Scene;

/// <summary>
/// A plot (story arc) — the <c>node_sharp_sys_scene_plots</c> vertex. Groups scenes
/// via <c>plot_includes</c> edges (one plot → many scenes). The owner is a graph edge
/// to the real object plus an <see cref="OwnerName"/> snapshot.
/// </summary>
/// <param name="Id">Storage key.</param>
/// <param name="Title">Plot title.</param>
/// <param name="Description">Plot summary / description.</param>
/// <param name="OwnerDbref">Live owner dbref resolved from the plotowner edge, or null if the object is gone.</param>
/// <param name="OwnerName">Snapshot of the owner's name.</param>
/// <param name="CreatedAt">UTC Unix-millis the plot was created.</param>
/// <param name="UpdatedAt">UTC Unix-millis of the most recent change.</param>
public record ScenePlot(
	string Id,
	string Title,
	string Description,
	string? OwnerDbref,
	string OwnerName,
	long CreatedAt,
	long UpdatedAt);
