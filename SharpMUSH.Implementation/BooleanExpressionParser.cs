using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using System.Linq.Expressions;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Implementation;

public class BooleanExpressionParser(
	IMediator mediator,
	[FromKeyedServices("compiled-expressions")] IFusionCache cache) : IBooleanExpressionParser
{
	// Keep in sync with the registration constant in Startup.cs (CompiledExpressionsCacheName).
	private const string CompiledExpressionsCacheName = "compiled-expressions";
	private const string CompiledExpressionsTag = "compiled-lock-expressions";
	private const string CacheKeyPrefix = "compiled-lock-expr:";

	/// <summary>
	/// Per-entry cache options for compiled lock expressions.
	/// <list type="bullet">
	///   <item><description><b>Duration 1 h</b> – absolute ceiling before eviction.</description></item>
	///   <item><description><b>EagerRefreshThreshold 0.9</b> – when an entry reaches 90 % of its TTL
	///     and is accessed, FusionCache silently recompiles it in the background so hot entries never
	///     suffer a cold-start compile stall. This effectively favours frequently-accessed lock
	///     expressions over rarely-called ones.</description></item>
	///   <item><description><b>Size 1</b> – each entry counts as one unit against the dedicated
	///     MemoryCache SizeLimit (1 024), capping the total number of cached expressions.</description></item>
	/// </list>
	/// </summary>
	private static readonly FusionCacheEntryOptions CompiledExpressionEntryOptions = new()
	{
		Duration = TimeSpan.FromHours(1),
		EagerRefreshThreshold = 0.9f,
		Size = 1,
	};

	/// <summary>
	/// Returns a compiled delegate for the given lock expression text, using FusionCache to avoid
	/// repeated ANTLR lex-parse-visit + Expression.Lambda.Compile() work on the hot path.
	/// Entries are tagged so the entire compiled-expression set can be flushed at once.
	/// </summary>
	public Func<AnySharpObject, AnySharpObject, bool> Compile(string text)
		=> cache.GetOrSet(
			$"{CacheKeyPrefix}{text}",
			_ => CompileInternal(text),
			CompiledExpressionEntryOptions,
			tags: [CompiledExpressionsTag])!;

	/// <summary>
	/// Invalidate a cached compiled expression. Call this when a lock expression changes
	/// (e.g., via @lock or @unlock). If text is null, clears all compiled expressions.
	/// </summary>
	public void InvalidateCache(string? text = null)
	{
		if (text is null)
		{
			cache.RemoveByTag(CompiledExpressionsTag);
			return;
		}

		cache.Remove($"{CacheKeyPrefix}{text}");
	}

	/// <summary>
	/// Builds a lock recognizer with ANTLR's default <c>ConsoleErrorListener</c> replaced by a
	/// collecting one on the parser, and removed outright from the lexer.
	/// <para>
	/// Unlike softcode, lock expressions can genuinely fail to parse — a bare <c>^</c> matches no
	/// lexer rule, and operators with nothing to join are a syntax error — so leaving the default
	/// listeners attached wrote player-triggered diagnostics to the server's stdout, where nobody
	/// sees them.
	/// </para>
	/// </summary>
	private static (SharpMUSHBoolExpParser Parser, ParserErrorListener Errors) CreateParser(string text, string origin)
	{
		var errors = new ParserErrorListener(text);

		StringSpanInputStream inputStream = new(text, origin);
		SharpMUSHBoolExpLexer lexer = new(inputStream)
		{
			TokenFactory = OptimizedTokenFactory.Default
		};
		lexer.RemoveErrorListeners();
		// The lock lexer is not total (a stray '^' matches no rule), so collect its token-recognition
		// errors too — otherwise a trailing bad character is dropped silently and the truncated stream
		// can still parse (e.g. "#TRUE^" -> "#TRUE"), so Validate/Compile would not fail closed.
		lexer.AddErrorListener(errors);

		BufferedTokenSpanStream tokenStream = new(lexer);
		SharpMUSHBoolExpParser parser = new(tokenStream);
		parser.RemoveErrorListeners();
		parser.AddErrorListener(errors);

		return (parser, errors);
	}

	private Func<AnySharpObject, AnySharpObject, bool> CompileInternal(string text)
	{
		var (sharpParser, errors) = CreateParser(text, nameof(Compile));
		var chatContext = sharpParser.@lock();

		// A malformed lock must never grant access. Validate gates storage at @lock time, but
		// Compile can still be handed text that never passed through it (a lock written before this
		// validation existed, or set through another path), and the visitor below assumes a
		// well-formed tree. Fail closed rather than compiling ANTLR's error-recovery tree into a
		// delegate that silently means something other than what was typed.
		if (errors.HasErrors)
		{
			return static (_, _) => false;
		}

		var parameter = Expression.Parameter(typeof(AnySharpObject), "gated");
		var parameter2 = Expression.Parameter(typeof(AnySharpObject), "unlocker");
		SharpMUSHBooleanExpressionVisitor visitor = new(mediator, parameter, parameter2);
		var expression = visitor.Visit(chatContext);

		return Expression.Lambda<Func<AnySharpObject, AnySharpObject, bool>>(expression, parameter, parameter2).Compile();
	}

	/// <summary>
	/// Validate that the expression is valid.
	/// </summary>
	/// <param name="expression"></param>
	/// <param name="lockee">Person to lock.</param>
	/// <returns>Valid or not.</returns>
	public bool Validate(string expression, AnySharpObject lockee)
	{
		var (sharpParser, errors) = CreateParser(expression, nameof(Validate));
		var chatContext = sharpParser.@lock();

		// A malformed expression is not a valid lock. Without this the visitor ran over ANTLR's
		// error-recovery tree and reported whatever survived as valid, so LockService.Set — which
		// gates on this method — stored locks that could never mean what was typed.
		if (errors.HasErrors)
		{
			return false;
		}

		SharpMUSHBooleanExpressionValidationVisitor visitor = new(lockee);

		var valid = visitor.Visit(chatContext)!.Value;

		return valid;
	}

	/// <summary>
	/// Normalizes a lock expression to canonical form, resolving object names to dbrefs.
	/// PennMUSH resolves all lock targets to dbrefs at @lock time.
	/// </summary>
	/// <param name="text">The lock expression to normalize</param>
	/// <param name="executor">The object setting the lock, used for name resolution context. If null, name resolution is skipped.</param>
	/// <returns>The normalized lock expression with names resolved to dbrefs</returns>
	public string Normalize(string text, AnySharpObject? executor = null)
	{
		var (sharpParser, errors) = CreateParser(text, nameof(Normalize));
		var chatContext = sharpParser.@lock();

		// If the expression does not parse there is no canonical form to produce; return it
		// unchanged rather than serialising an error-recovery tree into a bogus "normalized" lock.
		if (errors.HasErrors)
		{
			return text;
		}

		SharpMUSHBooleanExpressionNormalizationVisitor visitor = new(mediator, executor);

		var normalized = visitor.Visit(chatContext);

		return normalized;
	}
}
