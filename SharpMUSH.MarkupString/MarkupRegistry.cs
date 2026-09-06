using System.Collections.Frozen;
namespace MarkupString;

/// <summary>
/// The immutable set of emitters, framers and codecs a render or a (de)serialisation runs against.
/// Build one from <see cref="Empty"/> with <c>With(...)</c> — each call returns a new registry —
/// and hand it to <see cref="MarkupText.Render(MarkupFormat, MarkupRegistry?)"/>, or install it
/// once at startup as <see cref="Default"/>.
/// </summary>
public sealed class MarkupRegistry
{
	private readonly FrozenDictionary<(Type MarkupType, MarkupFormat Format), IMarkupEmitter> _emitters;
	private readonly FrozenDictionary<MarkupFormat, IMarkupSetEmitter> _setEmitters;
	private readonly FrozenDictionary<MarkupFormat, IFormatFramer> _framers;
	private readonly FrozenDictionary<string, IMarkupCodec> _codecsByKind;
	private readonly FrozenDictionary<Type, IMarkupCodec> _codecsByType;

	private MarkupRegistry(
		FrozenDictionary<(Type, MarkupFormat), IMarkupEmitter> emitters,
		FrozenDictionary<MarkupFormat, IMarkupSetEmitter> setEmitters,
		FrozenDictionary<MarkupFormat, IFormatFramer> framers,
		FrozenDictionary<string, IMarkupCodec> codecsByKind,
		FrozenDictionary<Type, IMarkupCodec> codecsByType)
	{
		_emitters = emitters;
		_setEmitters = setEmitters;
		_framers = framers;
		_codecsByKind = codecsByKind;
		_codecsByType = codecsByType;
	}

	/// <summary>A registry that knows nothing: every markup passes its body through unchanged.</summary>
	public static readonly MarkupRegistry Empty = new(
		FrozenDictionary<(Type, MarkupFormat), IMarkupEmitter>.Empty,
		FrozenDictionary<MarkupFormat, IMarkupSetEmitter>.Empty,
		FrozenDictionary<MarkupFormat, IFormatFramer>.Empty,
		FrozenDictionary<string, IMarkupCodec>.Empty,
		FrozenDictionary<Type, IMarkupCodec>.Empty);

	private static MarkupRegistry? _default;

	/// <summary>
	/// The registry used when a render or a (de)serialisation is not given one. The host sets it
	/// once at startup; reading it before then throws.
	/// </summary>
	/// <exception cref="InvalidOperationException">The registry was never set.</exception>
	public static MarkupRegistry Default
	{
		get => _default ?? throw new InvalidOperationException(
			"MarkupRegistry.Default has not been configured. Call MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml() at startup.");
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			_default = value;
		}
	}

	/// <summary>Whether <see cref="Default"/> has been set.</summary>
	public static bool IsConfigured => _default is not null;

	/// <summary>Returns a registry with <paramref name="emitter"/> added, replacing any emitter for the same type and format.</summary>
	public MarkupRegistry With(IMarkupEmitter emitter)
	{
		ArgumentNullException.ThrowIfNull(emitter);
		var emitters = new Dictionary<(Type, MarkupFormat), IMarkupEmitter>(_emitters.Count + 1);
		foreach (var pair in _emitters) emitters[pair.Key] = pair.Value;
		emitters[(emitter.MarkupType, emitter.Format)] = emitter;
		return new MarkupRegistry(emitters.ToFrozenDictionary(), _setEmitters, _framers, _codecsByKind, _codecsByType);
	}

	/// <summary>Returns a registry with <paramref name="emitter"/> added, replacing any set emitter for the same format.</summary>
	public MarkupRegistry With(IMarkupSetEmitter emitter)
	{
		ArgumentNullException.ThrowIfNull(emitter);
		var setEmitters = new Dictionary<MarkupFormat, IMarkupSetEmitter>(_setEmitters.Count + 1);
		foreach (var pair in _setEmitters) setEmitters[pair.Key] = pair.Value;
		setEmitters[emitter.Format] = emitter;
		return new MarkupRegistry(_emitters, setEmitters.ToFrozenDictionary(), _framers, _codecsByKind, _codecsByType);
	}

	/// <summary>Returns a registry with <paramref name="framer"/> added, replacing any framer for the same format.</summary>
	public MarkupRegistry With(IFormatFramer framer)
	{
		ArgumentNullException.ThrowIfNull(framer);
		var framers = new Dictionary<MarkupFormat, IFormatFramer>(_framers.Count + 1);
		foreach (var pair in _framers) framers[pair.Key] = pair.Value;
		framers[framer.Format] = framer;
		return new MarkupRegistry(_emitters, _setEmitters, framers.ToFrozenDictionary(), _codecsByKind, _codecsByType);
	}

	/// <summary>Returns a registry with <paramref name="codec"/> added, replacing any codec for the same kind or markup type.</summary>
	public MarkupRegistry With(IMarkupCodec codec)
	{
		ArgumentNullException.ThrowIfNull(codec);
		var byKind = new Dictionary<string, IMarkupCodec>(_codecsByKind.Count + 1, StringComparer.Ordinal);
		foreach (var pair in _codecsByKind) byKind[pair.Key] = pair.Value;
		byKind[codec.Kind] = codec;
		var byType = new Dictionary<Type, IMarkupCodec>(_codecsByType.Count + 1);
		foreach (var pair in _codecsByType) byType[pair.Key] = pair.Value;
		byType[codec.MarkupType] = codec;
		return new MarkupRegistry(_emitters, _setEmitters, _framers, byKind.ToFrozenDictionary(StringComparer.Ordinal), byType.ToFrozenDictionary());
	}

	/// <summary>The emitter for <paramref name="markupType"/> in <paramref name="format"/>, or <see langword="null"/>.</summary>
	public IMarkupEmitter? FindEmitter(Type markupType, MarkupFormat format) =>
		_emitters.TryGetValue((markupType, format), out var emitter) ? emitter : null;

	/// <summary>The set emitter for <paramref name="format"/>, or <see langword="null"/>.</summary>
	public IMarkupSetEmitter? FindSetEmitter(MarkupFormat format) =>
		_setEmitters.TryGetValue(format, out var emitter) ? emitter : null;

	/// <summary>The framer for <paramref name="format"/>, or <see langword="null"/>.</summary>
	public IFormatFramer? FindFramer(MarkupFormat format) =>
		_framers.TryGetValue(format, out var framer) ? framer : null;

	/// <summary>The codec for a wire kind, or <see langword="null"/>.</summary>
	public IMarkupCodec? FindCodec(string kind) =>
		_codecsByKind.TryGetValue(kind, out var codec) ? codec : null;

	/// <summary>The codec for a markup type, or <see langword="null"/>.</summary>
	public IMarkupCodec? FindCodec(Type markupType) =>
		_codecsByType.TryGetValue(markupType, out var codec) ? codec : null;
}
