using System.Globalization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.ParserInterfaces;

public record CallState(MString? Message, int Depth, MString[]? Arguments, Func<Task<MString?>> ParsedMessage)
{
    public static implicit operator CallState(MString? m) => new(m);
    public static implicit operator CallState(DBRef m) => new(m);
    public static implicit operator CallState(AnySharpObject m) => new(m.Object().DBRef);
    public static implicit operator CallState(bool m) => new(m);
    public static implicit operator CallState(int m) => new(m);
    public static implicit operator CallState(double m) => new(m);
    public static implicit operator CallState(decimal m) => new(m);
    public static implicit operator CallState(string m) => new(m);

	public CallState(MString? Message, int Depth)
		: this(Message ?? MModule.empty(), Depth, null, () => Task.FromResult(Message)) { }

	public CallState(MString? Message)
		: this(Message ?? MModule.empty(), 0, null, () => Task.FromResult(Message)) { }

	public CallState(int Message) : this(Message.ToString()) { }
	
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
				? () => Task.FromResult(MModule.single(Message))!
				: () => Task.FromResult(MModule.empty())!)
	{
	}

	public CallState(bool result, string errorIfFalse = "0") :
		this(MModule.single(result ? "1" : errorIfFalse), 0, null,
			() => Task.FromResult(MModule.single(result ? "1" : errorIfFalse))!)
	{
	}

	public CallState(string Message, int Depth)
		: this(!string.IsNullOrEmpty(Message)
				? MModule.single(Message)
				: MModule.empty(), Depth, null,
			!string.IsNullOrEmpty(Message)
				? () => Task.FromResult(MModule.single(Message))!
				: () => Task.FromResult(MModule.empty())!)
	{
	}

	public static readonly CallState EmptyArgument = new(MModule.empty(), 0, [], () => Task.FromResult(MModule.empty())!);
	public static readonly CallState Empty = new(MModule.empty(), 0, null, () => Task.FromResult(MModule.empty())!);
}