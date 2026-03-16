using Mediator;
using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SendMailCommand(SharpObject Sender, SharpPlayer Recipient, SharpMail Mail) : ICommand;

public record UpdateMailCommand(SharpMail Mail, MailUpdate Update) : ICommand;

public record DeleteMailCommand(SharpMail Mail) : ICommand;

public record RenameMailFolderCommand(SharpPlayer Owner, string FolderName, string NewFolderName) : ICommand;

public record MoveMailFolderCommand(SharpMail Mail, string NewFolderName) : ICommand;

[GenerateOneOf]
public class MailUpdate : OneOfBase<bool?, bool?, bool?, bool?>
{
	private MailUpdate(OneOf<bool?, bool?, bool?, bool?> input) : base(input) { }

	public static MailUpdate ReadEdit(bool read) => new(OneOf<bool?, bool?, bool?, bool?>.FromT0(read));
	public static MailUpdate ClearEdit(bool clear) => new(OneOf<bool?, bool?, bool?, bool?>.FromT1(clear));
	public static MailUpdate TaggedEdit(bool tagged) => new(OneOf<bool?, bool?, bool?, bool?>.FromT2(tagged));
	public static MailUpdate UrgentEdit(bool urgent) => new(OneOf<bool?, bool?, bool?, bool?>.FromT3(urgent));

	public bool IsReadEdit => IsT0;
	public bool IsClearEdit => IsT1;
	public bool IsTaggedEdit => IsT2;
	public bool IsUrgentEdit => IsT3;

	public bool AsReadEdit => AsT0!.Value;

	public bool AsClearEdit => AsT1!.Value;

	public bool AsTaggedEdit => AsT2!.Value;

	public bool AsUrgentEdit => AsT3!.Value;
}