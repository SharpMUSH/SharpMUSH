using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using System.Globalization;

namespace SharpMUSH.Library.ParserInterfaces;

public record CallState(MString? Message, int Depth, MString[]? Arguments, Func<ValueTask<MString?>> ParsedMessage)
{
	public static implicit operator CallState(MString? m) => new(m);
	public static implicit operator CallState(DBRef m) => new(m);
	public static implicit operator CallState(AnySharpObject m) => new(m.Object().DBRef);
	public static implicit operator CallState(bool m) => new(m);
	public static implicit operator CallState(int m) => new(m);
	public static implicit operator CallState(long m) => new(m);
	public static implicit operator CallState(double m) => new(m);
	public static implicit operator CallState(decimal m) => new(m);
	public static implicit operator CallState(string m) => new(m);
	public static implicit operator CallState(Error<string> m) => new(m.Value);

	public CallState(MString? Message, int Depth)
		: this(Message ?? MModule.empty(), Depth, null, () => ValueTask.FromResult(Message)) { }

	public CallState(MString? Message)
		: this(Message ?? MModule.empty(), 0, null, () => ValueTask.FromResult(Message)) { }

	public CallState(int Message) : this(Message.ToString()) { }

	public CallState(long Message) : this(Message.ToString()) { }

	public CallState(Error<string> Message) : this(Message.Value) { }

	public CallState(DBRef Message) : this(Message.ToString()) { }

	public CallState(double Message) : this(Message.ToString(CultureInfo.InvariantCulture)) { }

	public CallState(decimal Message) : this(Message.ToString(CultureInfo.InvariantCulture)) { }

	public CallState(string Message)
		: this(
			!string.IsNullOrEmpty(Message)
				? MModule.single(Message)
				: MModule.empty(),
			0, null,
			!string.IsNullOrEmpty(Message)
				? () => ValueTask.FromResult(MModule.single(Message))!
				: () => ValueTask.FromResult(MModule.empty())!)
	{
	}

	public CallState(bool result, string errorIfFalse = "0") :
		this(MModule.single(result ? "1" : errorIfFalse), 0, null,
			() => ValueTask.FromResult(MModule.single(result ? "1" : errorIfFalse))!)
	{
	}

	public CallState(string Message, int Depth)
		: this(!string.IsNullOrEmpty(Message)
				? MModule.single(Message)
				: MModule.empty(), Depth, null,
			!string.IsNullOrEmpty(Message)
				? () => ValueTask.FromResult(MModule.single(Message))!
				: () => ValueTask.FromResult(MModule.empty())!)
	{
	}

	public static readonly CallState EmptyArgument = new(MModule.empty(), 0, [], () => ValueTask.FromResult(MModule.empty())!);
	public static readonly CallState Empty = new(MModule.empty(), 0, null, () => ValueTask.FromResult(MModule.empty())!);
}