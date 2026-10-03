using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

/// <summary>A folder as PennMUSH names it: its number, and the name its messages are stored under.</summary>
public readonly record struct MailFolder(int Number, string Name, string DisplayName);

/// <summary>
/// A player's numbered mail folders (<see cref="ExpandedMailData"/>): reading the table, and changing it under the
/// mailbox's gate.
/// </summary>
public static class MailFolders
{
	/// <summary>
	/// Serializes reading a player's folder table with replacing it, since <c>SetExpandedDataAsync</c> stores the
	/// whole table and two writers numbering different new folders would otherwise each keep only their own.
	/// Striped by recipient so unrelated mailboxes do not wait on each other. Nothing that can deliver mail runs
	/// while a gate is held.
	/// </summary>
	private static readonly SemaphoreSlim[] MailboxGates = [.. Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1))];

	private static SemaphoreSlim GateFor(SharpPlayer player)
		=> MailboxGates[(int)((uint)player.Object.DBRef.Number % MailboxGates.Length)];

	public static async ValueTask<ExpandedMailData> LoadAsync(IExpandedObjectDataService objectData, SharpPlayer player)
		=> await objectData.GetExpandedDataAsync<ExpandedMailData>(player.Object) ?? new ExpandedMailData();

	/// <summary>The folder <paramref name="spec"/> names — a number or one of the player's folder names.</summary>
	public static Found<MailFolder> Resolve(ExpandedMailData data, string spec)
		=> data.Resolve(spec) is int number ? Folder(data, number) : new NotFound();

	public static MailFolder Folder(ExpandedMailData data, int number)
		=> new(number, data.FolderFor(number), data.DisplayName(number));

	/// <summary>
	/// The folder <paramref name="spec"/> names, for filing a message into: a number or one of the player's
	/// folders, or a new alphanumeric name, which becomes a folder with the lowest free number. Not found when
	/// the spec names no folder and none can be made.
	/// </summary>
	public static async ValueTask<Found<MailFolder>> FileTargetAsync(IExpandedObjectDataService objectData,
		SharpPlayer player, string spec)
	{
		spec = spec.Trim();
		var gate = GateFor(player);
		await gate.WaitAsync();
		try
		{
			var data = await LoadAsync(objectData, player);
			if (Resolve(data, spec) is MailFolder existing)
			{
				return await RecordedAsync(objectData, player, data, existing);
			}

			if (spec.Length == 0 || char.IsAsciiDigit(spec[0]) || !spec.All(char.IsAsciiLetterOrDigit)
					|| data.WithFolder(spec) is not ExpandedMailData numbered)
			{
				return new NotFound();
			}

			await SaveAsync(objectData, player, numbered with
			{
				Folders = [.. (numbered.Folders ?? []).Append(spec).Distinct()]
			});
			return Resolve(numbered, spec);
		}
		finally
		{
			gate.Release();
		}
	}

	/// <summary>
	/// Records an unnamed folder messages are about to be filed into, so the table lists every folder that holds
	/// mail.
	/// </summary>
	private static async ValueTask<Found<MailFolder>> RecordedAsync(IExpandedObjectDataService objectData,
		SharpPlayer player, ExpandedMailData data, MailFolder folder)
	{
		if (folder.Number != 0 && data.WithFolder(folder.Name) is ExpandedMailData numbered && numbered != data)
		{
			await SaveAsync(objectData, player, numbered);
		}

		return folder;
	}

	/// <summary>
	/// Gives folder <paramref name="number"/> the name <paramref name="name"/>, or takes its name away when
	/// <paramref name="name"/> is null, moving its messages to the name they are now stored under. The current
	/// folder moves with them.
	/// </summary>
	public static async ValueTask<MailFolder> RenameAsync(IExpandedObjectDataService objectData, IMediator mediator,
		SharpPlayer player, int number, string? name)
	{
		var gate = GateFor(player);
		await gate.WaitAsync();
		try
		{
			var data = await LoadAsync(objectData, player);
			var from = data.FolderFor(number);
			var to = name ?? number.ToString(System.Globalization.CultureInfo.InvariantCulture);
			if (!from.Equals(to, StringComparison.Ordinal))
			{
				await mediator.Send(new RenameMailFolderCommand(player, from, to));
			}

			var renamed = data.WithFolderNamed(number, to) with
			{
				Folders = [.. (data.Folders ?? []).Where(folder => folder != from).Append(to).Distinct()],
				ActiveFolder = data.ActiveFolder == from ? to : data.ActiveFolder
			};
			await SaveAsync(objectData, player, renamed);
			return Folder(renamed, number);
		}
		finally
		{
			gate.Release();
		}
	}

	/// <summary>Makes folder <paramref name="number"/> the player's current folder.</summary>
	public static async ValueTask<MailFolder> SetCurrentAsync(IExpandedObjectDataService objectData,
		SharpPlayer player, int number)
	{
		var gate = GateFor(player);
		await gate.WaitAsync();
		try
		{
			var data = await LoadAsync(objectData, player);
			var current = data with { ActiveFolder = data.FolderFor(number) };
			await SaveAsync(objectData, player, current);
			return Folder(current, number);
		}
		finally
		{
			gate.Release();
		}
	}

	private static ValueTask SaveAsync(IExpandedObjectDataService objectData, SharpPlayer player, ExpandedMailData data)
		=> objectData.SetExpandedDataAsync(data, player.Object, ignoreNull: true);
}
