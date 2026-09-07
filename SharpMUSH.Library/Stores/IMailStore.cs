using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// @mail: folders, incoming and sent mail, and mail updates.
/// </summary>
public interface IMailStore
{
	IAsyncEnumerable<SharpMail> GetIncomingMailsAsync(SharpPlayer id, string folder, CancellationToken cancellationToken = default);

	IAsyncEnumerable<SharpMail> GetAllIncomingMailsAsync(SharpPlayer id, CancellationToken cancellationToken = default);

	ValueTask<SharpMail?> GetIncomingMailAsync(SharpPlayer id, string folder, int mail, CancellationToken cancellationToken = default);

	IAsyncEnumerable<SharpMail> GetSentMailsAsync(SharpObject sender, SharpPlayer recipient, CancellationToken cancellationToken = default);

	IAsyncEnumerable<SharpMail> GetAllSentMailsAsync(SharpObject sender, CancellationToken cancellationToken = default);

	ValueTask<SharpMail?> GetSentMailAsync(SharpObject sender, SharpPlayer recipient, int mail, CancellationToken cancellationToken = default);

	ValueTask<string[]> GetMailFoldersAsync(SharpPlayer id, CancellationToken cancellationToken = default);

	ValueTask SendMailAsync(SharpObject from, SharpPlayer to, SharpMail mail, CancellationToken cancellationToken = default);

	ValueTask UpdateMailAsync(string mailId, MailUpdate commandMail, CancellationToken cancellationToken = default);

	ValueTask DeleteMailAsync(string mailId, CancellationToken cancellationToken = default);

	ValueTask RenameMailFolderAsync(SharpPlayer player, string folder, string newFolder, CancellationToken cancellationToken = default);

	ValueTask MoveMailFolderAsync(string mailId, string newFolder, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets ALL mail in the system regardless of owner or folder.
	/// WARNING: This bypasses all access controls and should only be used in God-level administrative operations.
	/// </summary>
	IAsyncEnumerable<SharpMail> GetAllSystemMailAsync(CancellationToken cancellationToken = default);
}
