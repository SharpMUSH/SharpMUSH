using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Components.Scenes;

/// <summary>A run of poses the reader hides, all of one type, drawn as one fold row that reveals them.</summary>
/// <param name="Type">The type the run is of.</param>
/// <param name="Poses">The poses it stands for, in order.</param>
public sealed record StoryFold(PoseTypeInfo Type, IReadOnlyList<ScenePoseView> Poses);

/// <summary>One thing a story draws: a pose, or a fold of hidden ones.</summary>
public union StoryEntry(ScenePoseView, StoryFold);

/// <summary>
/// Folds the poses of hidden types out of a story: each run of hidden poses of one type becomes a
/// <see cref="StoryFold"/> ("3 OOC lines hidden"), except the poses this view has revealed.
/// </summary>
public static class StoryFolds
{
	/// <param name="poses">The story in order.</param>
	/// <param name="typeOf">The type a key names (<c>PoseTypeService.For</c>).</param>
	/// <param name="isHidden">Whether the reader hides a type.</param>
	/// <param name="revealed">The ids of hidden poses this view shows anyway, because a fold was opened.</param>
	public static IReadOnlyList<StoryEntry> Fold(IEnumerable<ScenePoseView> poses, Func<string, PoseTypeInfo> typeOf,
		Func<string, bool> isHidden, IReadOnlySet<string> revealed)
	{
		var entries = new List<StoryEntry>();
		PoseTypeInfo? runType = null;
		List<ScenePoseView>? run = null;

		foreach (var pose in poses)
		{
			if (revealed.Contains(pose.Id) || !isHidden(pose.Type))
			{
				Flush();
				entries.Add(pose);
				continue;
			}

			var type = typeOf(pose.Type);
			if (run is null || runType is null || !string.Equals(runType.Key, type.Key, StringComparison.OrdinalIgnoreCase))
			{
				Flush();
				runType = type;
				run = [];
			}
			run.Add(pose);
		}

		Flush();
		return entries;

		void Flush()
		{
			if (run is { Count: > 0 } && runType is not null) entries.Add(new StoryFold(runType, run));
			run = null;
			runType = null;
		}
	}
}
