using Mediator;
using System.Runtime.CompilerServices;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SendMailCommand(SharpObject Sender, SharpPlayer Recipient, SharpMail Mail) : ICommand;

public record UpdateMailCommand(SharpMail Mail, MailUpdate Update) : ICommand;

public record DeleteMailCommand(SharpMail Mail) : ICommand;

public record RenameMailFolderCommand(SharpPlayer Owner, string FolderName, string NewFolderName) : ICommand;

public record MoveMailFolderCommand(SharpMail Mail, string NewFolderName) : ICommand;

/// <summary>
/// One change to a mail's status flags. Each flag is its own case so the four can never be confused,
/// although every one of them carries just the new setting.
/// </summary>
[Union]
public sealed class MailUpdate : IUnion
{
	public readonly record struct Read(bool Value);
	public readonly record struct Cleared(bool Value);
	public readonly record struct Tagged(bool Value);
	public readonly record struct Urgent(bool Value);

	public MailUpdate(Read value) => Value = value;
	public MailUpdate(Cleared value) => Value = value;
	public MailUpdate(Tagged value) => Value = value;
	public MailUpdate(Urgent value) => Value = value;

	public object? Value { get; }

	public override bool Equals(object? obj) => obj is MailUpdate other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public static MailUpdate ReadEdit(bool read) => new(new Read(read));
	public static MailUpdate ClearEdit(bool clear) => new(new Cleared(clear));
	public static MailUpdate TaggedEdit(bool tagged) => new(new Tagged(tagged));
	public static MailUpdate UrgentEdit(bool urgent) => new(new Urgent(urgent));

	public bool IsReadEdit => Value is Read;
	public bool IsClearEdit => Value is Cleared;
	public bool IsTaggedEdit => Value is Tagged;
	public bool IsUrgentEdit => Value is Urgent;

	public bool AsReadEdit => Value is Read edit ? edit.Value : throw UnionCase.Mismatch<Read>(Value);
	public bool AsClearEdit => Value is Cleared edit ? edit.Value : throw UnionCase.Mismatch<Cleared>(Value);
	public bool AsTaggedEdit => Value is Tagged edit ? edit.Value : throw UnionCase.Mismatch<Tagged>(Value);
	public bool AsUrgentEdit => Value is Urgent edit ? edit.Value : throw UnionCase.Mismatch<Urgent>(Value);
}
