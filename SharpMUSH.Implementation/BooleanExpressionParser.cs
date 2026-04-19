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

	private Func<AnySharpObject, AnySharpObject, bool> CompileInternal(string text)
	{
		AntlrInputStreamSpan inputStream = new(text.AsMemory(), nameof(Compile));
		SharpMUSHBoolExpLexer sharpLexer = new(inputStream);
		BufferedTokenSpanStream commonTokenStream = new(sharpLexer);
		SharpMUSHBoolExpParser sharpParser = new(commonTokenStream);
		var chatContext = sharpParser.@lock();
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
		AntlrInputStreamSpan inputStream = new(expression.AsMemory(), nameof(Validate));
		SharpMUSHBoolExpLexer sharpLexer = new(inputStream);
		BufferedTokenSpanStream commonTokenStream = new(sharpLexer);
		SharpMUSHBoolExpParser sharpParser = new(commonTokenStream);
		var chatContext = sharpParser.@lock();
		SharpMUSHBooleanExpressionValidationVisitor visitor = new(lockee);

		var valid = visitor.Visit(chatContext)!.Value;

		return valid;
	}

	/// <summary>
	/// Normalizes a lock expression by converting bare dbrefs to objids.
	/// This ensures locks reference specific object instances and won't match recycled dbrefs.
	/// </summary>
	/// <param name="text">The lock expression to normalize</param>
	/// <returns>The normalized lock expression with objids instead of bare dbrefs</returns>
	public string Normalize(string text)
	{
		AntlrInputStreamSpan inputStream = new(text.AsMemory(), nameof(Normalize));
		SharpMUSHBoolExpLexer sharpLexer = new(inputStream);
		BufferedTokenSpanStream commonTokenStream = new(sharpLexer);
		SharpMUSHBoolExpParser sharpParser = new(commonTokenStream);
		var chatContext = sharpParser.@lock();
		SharpMUSHBooleanExpressionNormalizationVisitor visitor = new(mediator);

		var normalized = visitor.Visit(chatContext);

		return normalized;
	}
}
