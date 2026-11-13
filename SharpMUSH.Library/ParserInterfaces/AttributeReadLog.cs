using MarkupString;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Softcode;

namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>
/// One single-attribute read a <c>memo()</c> answer depended on: who read what, how, and what it found
/// (<see cref="Fingerprint"/>). The answer stays good while the same read still finds the same thing.
/// </summary>
/// <param name="Executor">Who read it, whose permissions decided what was found.</param>
/// <param name="Object">The object it was read on.</param>
/// <param name="Attribute">The attribute name as the reader gave it.</param>
/// <param name="Mode">Read or Execute.</param>
/// <param name="Parent">Whether the read looked through parents.</param>
/// <param name="Fingerprint">What the read found: <see cref="AttributeReadLog.Absent"/>, an error, or the value with its markup.</param>
/// <param name="Dialect">The softcode dialect in force, which decides what an attribute alias names.</param>
public sealed record AttributeReadDependency(DBRef Executor, DBRef Object, string Attribute,
	IAttributeService.AttributeMode Mode, bool Parent, string Fingerprint, SoftcodeDialect? Dialect = null)
{
	/// <summary>
	/// What kind of read it was: one attribute (<see cref="Attribute"/> its name), the attributes matching a pattern
	/// (<see cref="Attribute"/> the pattern, <see cref="PatternMode"/> how it matches), or every visible attribute down
	/// to <see cref="Depth"/> levels. A listing's fingerprint is every name it found with its value.
	/// </summary>
	public AttributeReadKind Kind { get; init; } = AttributeReadKind.Single;

	/// <summary>How a <see cref="AttributeReadKind.Pattern"/> read matched.</summary>
	public IAttributeService.AttributePatternMode PatternMode { get; init; }

	/// <summary>How deep a <see cref="AttributeReadKind.Visible"/> read went.</summary>
	public int Depth { get; init; }

	/// <summary>An estimate of the memory this record holds, in bytes.</summary>
	public long Size => 64 + 2L * (Attribute.Length + Fingerprint.Length);
}

/// <summary>The kinds of read an <see cref="AttributeReadDependency"/> repeats.</summary>
public enum AttributeReadKind
{
	/// <summary>One attribute.</summary>
	Single,
	/// <summary>The attributes matching a pattern.</summary>
	Pattern,
	/// <summary>Every visible attribute.</summary>
	Visible
}

/// <summary>
/// The single-attribute reads made while a log is open, recorded by the attribute service. A log records into every
/// log enclosing it too, so a <c>memo()</c> inside another leaves its reads in both.
/// </summary>
public sealed class AttributeReadLog : IDisposable
{
	private static readonly AsyncLocal<AttributeReadLog?> Ambient = new();

	/// <summary>The fingerprint of a read that found no attribute.</summary>
	public const string Absent = "N";

	private readonly AttributeReadLog? _enclosing;
	private readonly bool _suspends;
	private readonly Lock _gate = new();
	private readonly List<AttributeReadDependency> _reads = [];

	private AttributeReadLog(AttributeReadLog? enclosing, bool suspends)
	{
		_enclosing = enclosing;
		_suspends = suspends;
	}

	/// <summary>Whether a log is open, so reads are worth fingerprinting.</summary>
	public static bool Recording => Ambient.Value is { _suspends: false };

	/// <summary>Opens a log until it is disposed.</summary>
	public static AttributeReadLog Open()
	{
		var log = new AttributeReadLog(Ambient.Value, suspends: false);
		Ambient.Value = log;
		return log;
	}

	/// <summary>Records nothing until the returned scope is disposed, for reads made to check a log rather than to answer.</summary>
	public static IDisposable Suspend()
	{
		var scope = new AttributeReadLog(Ambient.Value, suspends: true);
		Ambient.Value = scope;
		return scope;
	}

	/// <summary>Adds <paramref name="reads"/> to every open log.</summary>
	public static void Add(IEnumerable<AttributeReadDependency> reads)
	{
		if (!Recording) return;
		var list = reads as IReadOnlyCollection<AttributeReadDependency> ?? reads.ToList();
		for (var log = Ambient.Value; log is not null; log = log._enclosing)
		{
			if (log._suspends) return;
			lock (log._gate) log._reads.AddRange(list);
		}
	}

	private bool _untracked;

	/// <summary>
	/// Marks every open log as holding a read it cannot repeat (one made around the attribute service), so the answer
	/// built from it is not kept.
	/// </summary>
	public static void Untracked()
	{
		for (var log = Ambient.Value; log is not null && !log._suspends; log = log._enclosing)
		{
			log._untracked = true;
		}
	}

	/// <summary>Whether a read was made that this log cannot repeat.</summary>
	public bool IsUntracked => _untracked;

	/// <summary>The fingerprint of a listing: each attribute's name and value, in order, or the error.</summary>
	public static string Fingerprint(SharpAttributesOrError listing)
		=> listing.Value switch
		{
			SharpAttribute[] attributes => "L" + string.Join('\u0002',
				attributes.Select(attribute => attribute.LongName + '\u0001' + Fingerprint(attribute.Value))),
			Error<string> error => "E" + error.Value,
			_ => Absent
		};

	/// <summary>The reads recorded so far, each once.</summary>
	public IReadOnlyList<AttributeReadDependency> Reads
	{
		get
		{
			lock (_gate) return _reads.Distinct().ToList();
		}
	}

	/// <summary>The fingerprint of a value: its text, and its markup when it has any.</summary>
	public static string Fingerprint(MarkupText value)
		=> value.Runs.IsDefaultOrEmpty ? "P" + value.Text : "M" + MarkupTextSerializer.Serialize(value);

	/// <inheritdoc />
	public void Dispose() => Ambient.Value = _enclosing;
}
