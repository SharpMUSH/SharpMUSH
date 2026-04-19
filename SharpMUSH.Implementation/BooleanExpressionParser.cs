using Mediator;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace SharpMUSH.Implementation;

public class BooleanExpressionParser(IMediator mediator) : IBooleanExpressionParser
{
	private const int MaxCompiledExpressionCacheEntries = 4096;
	private const int CacheEvictionBatchSize = MaxCompiledExpressionCacheEntries / 4;
	private static readonly object _cacheTrimLock = new();
	private static readonly Queue<string> _cacheInsertionOrder = new();

	/// <summary>
	/// Cache of compiled lock expressions keyed by their text representation.
	/// Lock expressions are static text stored on objects that change only when a player
	/// explicitly sets a lock (via @lock). Since compilation involves a full ANTLR
	/// lex-parse-visit cycle plus Expression.Lambda.Compile() (JIT compilation),
	/// caching provides significant savings on the frequent lock-check hot path.
	/// 
	/// Uses Lazy&lt;T&gt; to ensure only one thread performs the expensive compilation
	/// for a given key, even under concurrent access.
	/// 
	/// The cache is bounded to avoid unbounded memory growth from arbitrary player-provided
	/// lock text. When the cap is reached and a new key is introduced, the oldest keys
	/// are evicted in batches to keep hot entries resident and avoid full-cache flushes.
	/// </summary>
	private static readonly ConcurrentDictionary<string, Lazy<Func<AnySharpObject, AnySharpObject, bool>>> _compiledCache = new();

	public Func<AnySharpObject, AnySharpObject, bool> Compile(string text)
	{
		if (!_compiledCache.TryGetValue(text, out var lazy))
		{
			var candidate = new Lazy<Func<AnySharpObject, AnySharpObject, bool>>(
				() => CompileInternal(text), LazyThreadSafetyMode.ExecutionAndPublication);

			if (_compiledCache.TryAdd(text, candidate))
			{
				TrackCacheKeyAndTrimIfNeeded(text);
				lazy = candidate;
			}
			else
			{
				lazy = _compiledCache[text];
			}
		}

		try
		{
			return lazy.Value;
		}
		catch
		{
			// Remove poisoned entry so subsequent attempts can retry
			_compiledCache.TryRemove(text, out _);
			throw;
		}
	}

	private static void TrackCacheKeyAndTrimIfNeeded(string key)
	{
		lock (_cacheTrimLock)
		{
			_cacheInsertionOrder.Enqueue(key);
			if (_compiledCache.Count <= MaxCompiledExpressionCacheEntries)
			{
				return;
			}

			for (var i = 0; i < CacheEvictionBatchSize && _compiledCache.Count > MaxCompiledExpressionCacheEntries && _cacheInsertionOrder.Count > 0; i++)
			{
				var removeKey = _cacheInsertionOrder.Dequeue();
				_compiledCache.TryRemove(removeKey, out _);
			}
		}
	}

	/// <summary>
	/// Invalidate a cached compiled expression. Call this when a lock expression changes
	/// (e.g., via @lock or @unlock). If text is null, clears the entire cache.
	/// </summary>
	public static void InvalidateCache(string? text = null)
	{
		if (text is null)
			_compiledCache.Clear();
		else
			_compiledCache.TryRemove(text, out _);
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
