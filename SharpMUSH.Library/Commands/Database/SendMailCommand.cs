using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SendMailCommand(SharpObject Sender, SharpPlayer Recipient, SharpMail Mail) : ICommand;

public record UpdateMailCommand(SharpMail Mail, MailUpdate Update) : ICommand;

public record DeleteMailCommand(SharpMail Mail) : ICommand;

public record RenameMailFolderCommand(SharpPlayer Owner, string FolderName, string NewFolderName) : ICommand;

public record MoveMailFolderCommand(SharpMail Mail, string NewFolderName) : ICommand;

public record MailReadEdit(bool Value);
public record MailClearEdit(bool Value);
public record MailTaggedEdit(bool Value);
public record MailUrgentEdit(bool Value);

public union MailUpdate(MailReadEdit, MailClearEdit, MailTaggedEdit, MailUrgentEdit)
{
	public bool IsReadEdit  => Value is MailReadEdit;
	public bool IsClearEdit  => Value is MailClearEdit;
	public bool IsTaggedEdit => Value is MailTaggedEdit;
	public bool IsUrgentEdit => Value is MailUrgentEdit;

	public bool AsReadEdit  => ((MailReadEdit)Value!).Value;
	public bool AsClearEdit  => ((MailClearEdit)Value!).Value;
	public bool AsTaggedEdit => ((MailTaggedEdit)Value!).Value;
	public bool AsUrgentEdit => ((MailUrgentEdit)Value!).Value;

	public static MailUpdate ReadEdit(bool read)   => new MailReadEdit(read);
	public static MailUpdate ClearEdit(bool clear)  => new MailClearEdit(clear);
	public static MailUpdate TaggedEdit(bool tagged) => new MailTaggedEdit(tagged);
	public static MailUpdate UrgentEdit(bool urgent) => new MailUrgentEdit(urgent);
}