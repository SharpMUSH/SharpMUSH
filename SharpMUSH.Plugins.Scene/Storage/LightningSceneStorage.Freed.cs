using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Storage;

/// <summary>
/// Letting go of freed objects. The store keeps objects by number, so before a freed object's number can name
/// another object everything here that names it is cleared: its places in casts, invitations and the member
/// index, and its number on the scenes, poses, edits and plots it owned, started, hosted or wrote. The name each
/// of those took when it was written stays, so a log still says who posed.
/// </summary>
public sealed partial class LightningSceneStorage : IFreedObjectCleanup
{
	public async ValueTask ObjectsFreedAsync(IReadOnlyCollection<DBRef> objids, CancellationToken cancellationToken = default)
	{
		var freed = objids.Select(objid => (long)objid.Number).ToHashSet();
		if (freed.Count == 0) return;
		await _accessor.WriteAsync(tx =>
		{
			ForgetObjects(tx, freed);
			return true;
		}, cancellationToken);
	}

	private void ForgetObjects(ITx tx, HashSet<long> freed)
	{
		foreach (var number in freed)
		{
			foreach (var sceneId in MemberScenes(tx, number))
			{
				tx.Delete(_participants, Keys.Composite(sceneId, number));
				RemoveIndex(tx, MemberIndexKey(number), sceneId);
			}

			foreach (var sceneId in tx.Dups(_sceneIdx, RoomIndexKey(number)).Select(id => Keys.ReadStr(id)).ToList())
			{
				RemoveIndex(tx, RoomIndexKey(number), sceneId);
			}
		}

		foreach (var (key, value) in tx.Range(_scenes, []).ToList())
		{
			var scene = Decode<SceneRecord>(value);
			var invited = scene.PendingInvitations.RemoveWhere(freed.Contains) > 0;
			if (!invited && !Freed(scene.OwnerDbref) && !Freed(scene.StarterDbref) && !Freed(scene.RoomDbref)) continue;
			if (Freed(scene.OwnerDbref)) scene.OwnerDbref = null;
			if (Freed(scene.StarterDbref)) scene.StarterDbref = null;
			if (Freed(scene.RoomDbref)) scene.RoomDbref = null;
			tx.Put(_scenes, key, Encode(scene));
		}

		foreach (var (key, value) in tx.Range(_poses, []).ToList())
		{
			var pose = Decode<ScenePoseRecord>(value);
			if (!Freed(pose.AuthorDbref) && !Freed(pose.OriginDbref)) continue;
			if (Freed(pose.AuthorDbref)) pose.AuthorDbref = null;
			if (Freed(pose.OriginDbref)) pose.OriginDbref = null;
			tx.Put(_poses, key, Encode(pose));
		}

		foreach (var (key, value) in tx.Range(_log, []).ToList())
		{
			var edit = Decode<ScenePoseEditRecord>(value);
			if (Freed(edit.EditorDbref)) tx.Put(_log, key, Encode(edit with { EditorDbref = null }));
		}

		foreach (var (key, value) in tx.Range(_plots, []).ToList())
		{
			var plot = Decode<ScenePlotRecord>(value);
			if (!Freed(plot.OwnerDbref)) continue;
			plot.OwnerDbref = null;
			tx.Put(_plots, key, Encode(plot));
		}

		bool Freed(long? number) => number is { } held && freed.Contains(held);
	}
}
