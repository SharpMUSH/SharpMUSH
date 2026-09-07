using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IMailStore"/>: @mail folders, incoming and sent mail. Not ported yet — every member
/// throws <see cref="NotImplementedException"/> until a later task.
/// </summary>
public sealed partial class LightningDatabase
{
	public IAsyncEnumerable<SharpMail> GetIncomingMailsAsync(SharpPlayer id, string folder, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpMail> GetAllIncomingMailsAsync(SharpPlayer id, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpMail?> GetIncomingMailAsync(SharpPlayer id, string folder, int mail, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpMail> GetSentMailsAsync(SharpObject sender, SharpPlayer recipient, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpMail> GetAllSentMailsAsync(SharpObject sender, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpMail?> GetSentMailAsync(SharpObject sender, SharpPlayer recipient, int mail, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<string[]> GetMailFoldersAsync(SharpPlayer id, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SendMailAsync(SharpObject from, SharpPlayer to, SharpMail mail, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UpdateMailAsync(string mailId, MailUpdate commandMail, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask DeleteMailAsync(string mailId, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask RenameMailFolderAsync(SharpPlayer player, string folder, string newFolder, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask MoveMailFolderAsync(string mailId, string newFolder, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpMail> GetAllSystemMailAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();
}
