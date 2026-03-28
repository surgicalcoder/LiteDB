using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace LiteDbX;

internal sealed class LiteDbXQueryable<T> : IOrderedQueryable<T>, ILiteDbXQueryableAccessor
{
    public LiteDbXQueryable(LiteDbXQueryProvider provider, LiteDbXQueryState state)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Expression = Expression.Constant(this);
        State = (state ?? throw new ArgumentNullException(nameof(state))).WithQueryExpression(Expression);
    }

    public LiteDbXQueryable(LiteDbXQueryProvider provider, Expression expression, LiteDbXQueryState state)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        State = (state ?? throw new ArgumentNullException(nameof(state))).WithQueryExpression(Expression);
    }

    public Type ElementType => typeof(T);

    public Expression Expression { get; }

    public IQueryProvider Provider { get; }

    public LiteDbXQueryState State { get; }

    public IEnumerator<T> GetEnumerator()
    {
        throw LiteDbXQueryProvider.CreateSyncExecutionException(State.WithTerminal(LiteDbXQueryTerminalKind.ToEnumerable, typeof(T)));
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

internal sealed class LiteDbXQueryProvider : IQueryProvider
{
    public LiteDbXQueryProvider(LiteDbXQueryRoot root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public LiteDbXQueryRoot Root { get; }

    public IQueryable CreateQuery(Expression expression)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));

        var state = Translate(expression);
        var elementType = LiteDbXQueryExpressionHelper.GetSequenceElementType(expression.Type) ?? state.CurrentElementType;

        return CreateQueryable(elementType, expression, state);
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));

        var state = Translate(expression);

        return new LiteDbXQueryable<TElement>(this, expression, state);
    }

    public object Execute(Expression expression)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));

        var state = TranslateTerminal(expression);

        throw CreateSyncExecutionException(state);
    }

    public TResult Execute<TResult>(Expression expression)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));

        var state = TranslateTerminal(expression);

        throw CreateSyncExecutionException(state);
    }

    internal LiteDbXQueryState Translate(Expression expression)
    {
        return LiteDbXQueryParser.Parse(expression, Root).WithQueryExpression(expression);
    }

    internal LiteDbXQueryState TranslateTerminal(Expression expression)
    {
        return LiteDbXQueryParser.ParseTerminal(expression, Root).WithQueryExpression(expression);
    }

    internal Query LowerToQuery(Expression expression)
    {
        return LiteDbXQueryLowerer.Lower(Translate(expression));
    }

    internal Query LowerToQuery(LiteDbXQueryState state)
    {
        return LiteDbXQueryLowerer.Lower(state ?? throw new ArgumentNullException(nameof(state)));
    }

    internal LiteQueryable<TElement> LowerToNativeQueryable<TElement>(Expression expression)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));

        return LowerToNativeQueryable<TElement>(Translate(expression));
    }

    internal LiteQueryable<TElement> LowerToNativeQueryable<TElement>(LiteDbXQueryState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        var query = LowerToQuery(state);

        return new LiteQueryable<TElement>(state.Root.Engine, state.Root.Mapper, state.Root.CollectionName, query, state.Root.Transaction);
    }

    internal static NotSupportedException CreateSyncExecutionException(LiteDbXQueryState state)
    {
        var operation = state == null || state.TerminalKind == LiteDbXQueryTerminalKind.None
            ? "synchronous LINQ execution"
            : state.TerminalKind.ToString();

        var detail = state?.Describe() ?? "LiteDbXQueryable";

        return new NotSupportedException(
            $"LiteDbX LINQ provider-backed queries do not support synchronous execution ({operation}). " +
            "Use the LiteDbX async queryable terminals such as ToListAsync(), FirstAsync(), AnyAsync(), CountAsync(), or LongCountAsync(), " +
            $"or fall back to collection.Query() for the native builder. State: {detail}");
    }

    private IQueryable CreateQueryable(Type elementType, Expression expression, LiteDbXQueryState state)
    {
        var method = typeof(LiteDbXQueryProvider)
            .GetMethod(nameof(CreateQueryableGeneric), BindingFlags.Instance | BindingFlags.NonPublic)
            ?.MakeGenericMethod(elementType);

        if (method == null)
        {
            throw new InvalidOperationException("Unable to construct the LiteDbX LINQ queryable wrapper.");
        }

        return (IQueryable)method.Invoke(this, new object[] { expression, state });
    }

    private IQueryable CreateQueryableGeneric<TElement>(Expression expression, LiteDbXQueryState state)
    {
        return new LiteDbXQueryable<TElement>(this, expression, state);
    }
}

internal static class LiteDbXQueryParser
{
    public static LiteDbXQueryState Parse(Expression expression, LiteDbXQueryRoot root)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));
        if (root == null) throw new ArgumentNullException(nameof(root));

        return expression switch
        {
            ConstantExpression constant => ParseConstant(constant, root),
            MethodCallExpression call => ParseMethodCall(call, root),
            _ => throw new NotSupportedException($"Expression shape {expression.NodeType} is not supported by the LiteDbX LINQ provider scaffold ({expression}).")
        };
    }

    public static LiteDbXQueryState ParseTerminal(Expression expression, LiteDbXQueryRoot root)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));
        if (root == null) throw new ArgumentNullException(nameof(root));

        if (expression is MethodCallExpression call && IsQueryableMethod(call.Method) && !IsQueryShapingMethod(call))
        {
            var source = Parse(call.Arguments[0], root);
            var terminalKind = GetTerminalKind(call.Method);

            if (terminalKind == null)
            {
                throw UnsupportedMethod(call.Method, expression);
            }

            return source.WithTerminal(terminalKind.Value, call.Type);
        }

        return Parse(expression, root);
    }

    private static LiteDbXQueryState ParseConstant(ConstantExpression constant, LiteDbXQueryRoot root)
    {
        if (constant.Value is ILiteDbXQueryableAccessor accessor)
        {
            return accessor.State;
        }

        if (constant.Value == null && typeof(IQueryable).IsAssignableFrom(constant.Type))
        {
            return LiteDbXQueryState.CreateRoot(root);
        }

        throw new NotSupportedException($"The LiteDbX LINQ provider requires a LiteDbX query root. Unsupported constant expression: {constant}.");
    }

    private static LiteDbXQueryState ParseMethodCall(MethodCallExpression call, LiteDbXQueryRoot root)
    {
        if (!IsQueryableMethod(call.Method))
        {
            throw UnsupportedMethod(call.Method, call);
        }

        if (!IsQueryShapingMethod(call))
        {
            return ParseTerminal(call, root);
        }

        var source = Parse(call.Arguments[0], root);
        var methodKind = GetMethodKind(call.Method);

        if (methodKind == null)
        {
            throw UnsupportedMethod(call.Method, call);
        }

        ValidateShape(source, methodKind.Value, call);

        return methodKind.Value switch
        {
            LiteDbXQueryMethodKind.Where => source.AppendOperator(
                new LiteDbXQueryOperator(methodKind.Value, call, lambda: LiteDbXQueryExpressionHelper.GetLambda(call.Arguments[1])),
                source.CurrentElementType,
                queryExpression: call),

            LiteDbXQueryMethodKind.GroupBy => source.AppendOperator(
                new LiteDbXQueryOperator(methodKind.Value, call, lambda: LiteDbXQueryExpressionHelper.GetLambda(call.Arguments[1]), resultType: LiteDbXQueryExpressionHelper.GetSequenceElementType(call.Type)),
                LiteDbXQueryExpressionHelper.GetSequenceElementType(call.Type) ?? throw new NotSupportedException($"Unable to determine GroupBy result type for expression {call}."),
                isGrouped: true,
                queryExpression: call),

            LiteDbXQueryMethodKind.Select => source.AppendOperator(
                new LiteDbXQueryOperator(methodKind.Value, call, lambda: LiteDbXQueryExpressionHelper.GetLambda(call.Arguments[1]), resultType: LiteDbXQueryExpressionHelper.GetSequenceElementType(call.Type)),
                LiteDbXQueryExpressionHelper.GetSequenceElementType(call.Type) ?? throw new NotSupportedException($"Unable to determine Select result type for expression {call}."),
                hasProjection: true,
                queryExpression: call),

            LiteDbXQueryMethodKind.OrderBy => source.AppendOperator(
                new LiteDbXQueryOperator(methodKind.Value, call, lambda: LiteDbXQueryExpressionHelper.GetLambda(call.Arguments[1])),
                source.CurrentElementType,
                queryExpression: call),

            LiteDbXQueryMethodKind.OrderByDescending => source.AppendOperator(
                new LiteDbXQueryOperator(methodKind.Value, call, lambda: LiteDbXQueryExpressionHelper.GetLambda(call.Arguments[1])),
                source.CurrentElementType,
                queryExpression: call),

            LiteDbXQueryMethodKind.ThenBy => source.AppendOperator(
                new LiteDbXQueryOperator(methodKind.Value, call, lambda: LiteDbXQueryExpressionHelper.GetLambda(call.Arguments[1])),
                source.CurrentElementType,
                queryExpression: call),

            LiteDbXQueryMethodKind.ThenByDescending => source.AppendOperator(
                new LiteDbXQueryOperator(methodKind.Value, call, lambda: LiteDbXQueryExpressionHelper.GetLambda(call.Arguments[1])),
                source.CurrentElementType,
                queryExpression: call),

            LiteDbXQueryMethodKind.Skip => source.AppendOperator(
                new LiteDbXQueryOperator(methodKind.Value, call, valueExpression: call.Arguments[1]),
                source.CurrentElementType,
                queryExpression: call),

            LiteDbXQueryMethodKind.Take => source.AppendOperator(
                new LiteDbXQueryOperator(methodKind.Value, call, valueExpression: call.Arguments[1]),
                source.CurrentElementType,
                queryExpression: call),

            _ => throw UnsupportedMethod(call.Method, call)
        };
    }

    private static bool IsQueryableMethod(MethodInfo method)
    {
        return method.DeclaringType == typeof(Queryable);
    }

    private static bool IsQueryShapingMethod(MethodCallExpression call)
    {
        return typeof(IQueryable).IsAssignableFrom(call.Type);
    }

    private static LiteDbXQueryMethodKind? GetMethodKind(MethodInfo method)
    {
        return method.Name switch
        {
            nameof(Queryable.Where) => LiteDbXQueryMethodKind.Where,
            nameof(Queryable.GroupBy) => LiteDbXQueryMethodKind.GroupBy,
            nameof(Queryable.Select) => LiteDbXQueryMethodKind.Select,
            nameof(Queryable.OrderBy) => LiteDbXQueryMethodKind.OrderBy,
            nameof(Queryable.OrderByDescending) => LiteDbXQueryMethodKind.OrderByDescending,
            nameof(Queryable.ThenBy) => LiteDbXQueryMethodKind.ThenBy,
            nameof(Queryable.ThenByDescending) => LiteDbXQueryMethodKind.ThenByDescending,
            nameof(Queryable.Skip) => LiteDbXQueryMethodKind.Skip,
            nameof(Queryable.Take) => LiteDbXQueryMethodKind.Take,
            _ => null
        };
    }

    private static LiteDbXQueryTerminalKind? GetTerminalKind(MethodInfo method)
    {
        return method.Name switch
        {
            nameof(Queryable.First) => LiteDbXQueryTerminalKind.First,
            nameof(Queryable.FirstOrDefault) => LiteDbXQueryTerminalKind.FirstOrDefault,
            nameof(Queryable.Single) => LiteDbXQueryTerminalKind.Single,
            nameof(Queryable.SingleOrDefault) => LiteDbXQueryTerminalKind.SingleOrDefault,
            nameof(Queryable.Any) => LiteDbXQueryTerminalKind.Any,
            nameof(Queryable.Count) => method.ReturnType == typeof(long)
                ? LiteDbXQueryTerminalKind.LongCount
                : LiteDbXQueryTerminalKind.Count,
            nameof(Queryable.LongCount) => LiteDbXQueryTerminalKind.LongCount,
            _ => null
        };
    }

    private static NotSupportedException UnsupportedMethod(MethodInfo method, Expression expression)
    {
        return new NotSupportedException(
            $"Queryable method {Reflection.MethodName(method)} is not supported by the current LiteDbX LINQ MVP scope ({expression}). " +
            "Supported operators in this phase are Where, Select, OrderBy, OrderByDescending, ThenBy, ThenByDescending, Skip, Take, and a narrow GroupBy(...)-with-grouped-aggregate-projection subset. " +
            "Fall back to collection.Query() for unsupported shapes.");
    }

    private static void ValidateShape(LiteDbXQueryState source, LiteDbXQueryMethodKind methodKind, MethodCallExpression call)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (call == null) throw new ArgumentNullException(nameof(call));

        if (methodKind == LiteDbXQueryMethodKind.GroupBy)
        {
            ValidateGroupByShape(source, call);
            return;
        }

        if (source.IsGrouped)
        {
            ValidateGroupedContinuation(source, methodKind, call);
        }

        if (source.HasProjection && methodKind != LiteDbXQueryMethodKind.Skip && methodKind != LiteDbXQueryMethodKind.Take)
        {
            throw UnsupportedPattern(
                "query-shaping after Select(...) is not supported in the current LiteDbX LINQ MVP",
                call,
                "Apply filtering and ordering before Select(...), or fall back to collection.Query() for more advanced query shapes.");
        }

        if (source.HasPaging && methodKind != LiteDbXQueryMethodKind.Skip && methodKind != LiteDbXQueryMethodKind.Take)
        {
            throw UnsupportedPattern(
                $"{methodKind} after paging is not supported in the current LiteDbX LINQ MVP",
                call,
                "Skip(...) / Take(...) must be the last query-shaping operators in the MVP provider pipeline.");
        }

        switch (methodKind)
        {
            case LiteDbXQueryMethodKind.Select when source.HasProjection:
                throw UnsupportedPattern(
                    "multiple Select(...) projections are not supported in the current LiteDbX LINQ MVP",
                    call,
                    "Use a single Select(...) projection, or fall back to collection.Query() for more advanced projection pipelines.");

            case LiteDbXQueryMethodKind.OrderBy:
            case LiteDbXQueryMethodKind.OrderByDescending:
                if (source.HasPrimaryOrdering)
                {
                    throw UnsupportedPattern(
                        "multiple primary ordering clauses are not supported by the LiteDbX LINQ MVP translator",
                        call,
                        "Use ThenBy(...) / ThenByDescending(...) to add secondary sort keys, or fall back to collection.Query().");
                }
                break;

            case LiteDbXQueryMethodKind.Skip:
                if (source.HasOffset)
                {
                    throw UnsupportedPattern(
                        "multiple Skip(...) operators are not supported in the current LiteDbX LINQ MVP",
                        call,
                        "Use a single Skip(...), or fall back to collection.Query() for non-MVP paging shapes.");
                }

                if (source.HasLimit && !source.HasOffset)
                {
                    throw UnsupportedPattern(
                        "Skip(...) after Take(...) is not supported in the current LiteDbX LINQ MVP",
                        call,
                        "Use Skip(...).Take(...) ordering in the MVP provider pipeline.");
                }
                break;

            case LiteDbXQueryMethodKind.Take:
                if (source.HasLimit)
                {
                    throw UnsupportedPattern(
                        "multiple Take(...) operators are not supported in the current LiteDbX LINQ MVP",
                        call,
                        "Use a single Take(...), or fall back to collection.Query() for non-MVP paging shapes.");
                }
                break;
        }
    }

    private static void ValidateGroupByShape(LiteDbXQueryState source, MethodCallExpression call)
    {
        if (source.IsGrouped)
        {
            throw UnsupportedPattern(
                "nested GroupBy(...) composition is not supported by the LiteDbX LINQ provider",
                call,
                "Support is intentionally limited to a single grouped query stage that lowers directly to collection.Query().GroupBy(...).");
        }

        if (source.HasProjection)
        {
            throw UnsupportedPattern(
                "GroupBy(...) after Select(...) is not supported by the LiteDbX LINQ provider",
                call,
                "Apply GroupBy(...) directly to the collection-root document shape, or fall back to collection.Query() for advanced grouped pipelines.");
        }

        if (source.HasAnyOrdering)
        {
            throw UnsupportedPattern(
                "GroupBy(...) does not support pre-group ordering in the LiteDbX LINQ provider",
                call,
                "The native engine routes grouped queries through its own key-ordered group-by pipeline. Remove OrderBy(...), or use collection.Query() if you need manual grouped query composition.");
        }

        if (source.HasPaging)
        {
            throw UnsupportedPattern(
                "GroupBy(...) after Skip(...) / Take(...) is not supported by the LiteDbX LINQ provider",
                call,
                "The native engine applies paging after grouping. Use grouped Skip(...) / Take(...) after the grouped projection, or fall back to collection.Query().");
        }

        if (call.Arguments.Count != 2)
        {
            throw UnsupportedPattern(
                "Only Queryable.GroupBy(source, keySelector) is supported by the LiteDbX LINQ provider",
                call,
                "Element selectors, result selectors, and comparer overloads are intentionally deferred. Use collection.Query() for advanced grouped queries.");
        }
    }

    private static void ValidateGroupedContinuation(LiteDbXQueryState source, LiteDbXQueryMethodKind methodKind, MethodCallExpression call)
    {
        switch (methodKind)
        {
            case LiteDbXQueryMethodKind.Where:
                if (source.HasProjection)
                {
                    throw UnsupportedPattern(
                        "Where(...) after a grouped Select(...) is not supported by the LiteDbX LINQ provider",
                        call,
                        "Use post-group filtering before Select(...) so it can lower to HAVING, or fall back to collection.Query().");
                }
                return;

            case LiteDbXQueryMethodKind.Select:
                return;

            case LiteDbXQueryMethodKind.Skip:
            case LiteDbXQueryMethodKind.Take:
                return;

            case LiteDbXQueryMethodKind.OrderBy:
            case LiteDbXQueryMethodKind.OrderByDescending:
            case LiteDbXQueryMethodKind.ThenBy:
            case LiteDbXQueryMethodKind.ThenByDescending:
                throw UnsupportedPattern(
                    "Grouped LINQ queries do not support OrderBy(...) in the current LiteDbX provider",
                    call,
                    "The native group-by pipeline already orders by the group key when required. Keep grouped queries in engine order, or use collection.Query() for manual grouped execution.");

            case LiteDbXQueryMethodKind.GroupBy:
                throw UnsupportedPattern(
                    "Nested GroupBy(...) composition is not supported by the LiteDbX LINQ provider",
                    call,
                    "Only a single grouped stage that lowers directly to Query.GroupBy is supported.");

            default:
                throw UnsupportedPattern(
                    $"{methodKind} is not supported after GroupBy(...) in the LiteDbX LINQ provider",
                    call,
                    "Supported grouped continuations are limited to Where(...) for HAVING, a single grouped Select(...), and optional Skip(...) / Take(...). Fall back to collection.Query() for more advanced grouped composition.");
        }
    }

    private static NotSupportedException UnsupportedPattern(string pattern, Expression expression, string guidance)
    {
        return new NotSupportedException(
            $"{pattern} ({expression}). {guidance} Fall back to collection.Query() when you need native LiteDbX query-builder behavior outside the LINQ MVP scope.");
    }
}

internal static class LiteDbXQueryLowerer
{
    public static Query Lower(LiteDbXQueryState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        ValidateLoweringShape(state);

        var query = new Query();
        var groupByDefined = false;

        foreach (var include in state.Root.Includes)
        {
            query.Includes.Add(include);
        }

        foreach (var operation in state.Operators)
        {
            switch (operation.Kind)
            {
                case LiteDbXQueryMethodKind.Where:
                    if (groupByDefined)
                    {
                        var having = LiteDbXGroupedQueryTranslator.TranslateHaving(state.Root.Mapper, operation.Lambda);
                        query.Having = query.Having == null ? having : Query.And(query.Having, having);
                    }
                    else
                    {
                        query.Where.Add(LiteDbXLambdaTranslator.Translate(state.Root.Mapper, operation.Lambda));
                    }
                    break;

                case LiteDbXQueryMethodKind.GroupBy:
                    query.GroupBy = LiteDbXLambdaTranslator.Translate(state.Root.Mapper, operation.Lambda);
                    groupByDefined = true;
                    break;

                case LiteDbXQueryMethodKind.Select:
                    query.Select = groupByDefined
                        ? LiteDbXGroupedQueryTranslator.TranslateSelect(state.Root.Mapper, operation.Lambda)
                        : LiteDbXLambdaTranslator.Translate(state.Root.Mapper, operation.Lambda);
                    break;

                case LiteDbXQueryMethodKind.OrderBy:
                    if (query.OrderBy.Count > 0)
                    {
                        throw new InvalidOperationException("ORDER BY is already defined in the lowered LiteDbX query.");
                    }

                    query.OrderBy.Add(new QueryOrder(LiteDbXLambdaTranslator.Translate(state.Root.Mapper, operation.Lambda), Query.Ascending));
                    break;

                case LiteDbXQueryMethodKind.OrderByDescending:
                    if (query.OrderBy.Count > 0)
                    {
                        throw new InvalidOperationException("ORDER BY is already defined in the lowered LiteDbX query.");
                    }

                    query.OrderBy.Add(new QueryOrder(LiteDbXLambdaTranslator.Translate(state.Root.Mapper, operation.Lambda), Query.Descending));
                    break;

                case LiteDbXQueryMethodKind.ThenBy:
                    query.OrderBy.Add(new QueryOrder(LiteDbXLambdaTranslator.Translate(state.Root.Mapper, operation.Lambda), Query.Ascending));
                    break;

                case LiteDbXQueryMethodKind.ThenByDescending:
                    query.OrderBy.Add(new QueryOrder(LiteDbXLambdaTranslator.Translate(state.Root.Mapper, operation.Lambda), Query.Descending));
                    break;

                case LiteDbXQueryMethodKind.Skip:
                    query.Offset = LiteDbXQueryExpressionHelper.Evaluate<int>(operation.ValueExpression);
                    break;

                case LiteDbXQueryMethodKind.Take:
                    query.Limit = LiteDbXQueryExpressionHelper.Evaluate<int>(operation.ValueExpression);
                    break;

                default:
                    throw new NotSupportedException($"LiteDbX query operator {operation.Kind} is not supported by the current lowering scaffold.");
            }
        }

        return query;
    }

    private static void ValidateLoweringShape(LiteDbXQueryState state)
    {
        if (!state.IsGrouped)
        {
            return;
        }

        if (!state.HasProjection)
        {
            throw new NotSupportedException(
                $"Raw GroupBy(...) materialization is not supported by the LiteDbX LINQ provider ({state.QueryExpression}). " +
                "Support is intentionally limited to grouped aggregate projections such as GroupBy(...).Select(g => new { g.Key, Count = g.Count() }). " +
                "Use collection.Query() for advanced/manual grouped queries.");
        }

        if (state.TerminalKind == LiteDbXQueryTerminalKind.Any ||
            state.TerminalKind == LiteDbXQueryTerminalKind.Count ||
            state.TerminalKind == LiteDbXQueryTerminalKind.LongCount)
        {
            throw new NotSupportedException(
                $"{state.TerminalKind} over grouped LINQ queries is not supported by the current LiteDbX provider ({state.QueryExpression}). " +
                "The native Count/Any helpers aggregate source documents rather than grouped result rows. Materialize the grouped projection with ToListAsync()/ToArrayAsync(), or use collection.Query() for manual grouped execution.");
        }
    }
}

internal static class LiteDbXLambdaTranslator
{
    private static readonly MethodInfo GetExpressionMethod = typeof(BsonMapper)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public)
        .Single(x => x.Name == nameof(BsonMapper.GetExpression) && x.IsGenericMethodDefinition && x.GetParameters().Length == 1);

    public static BsonExpression Translate(BsonMapper mapper, LambdaExpression lambda)
    {
        if (mapper == null) throw new ArgumentNullException(nameof(mapper));
        if (lambda == null) throw new ArgumentNullException(nameof(lambda));
        if (lambda.Parameters.Count != 1) throw new NotSupportedException($"Only single-parameter lambdas are supported by the LiteDbX LINQ provider scaffold ({lambda}).");

        var method = GetExpressionMethod.MakeGenericMethod(lambda.Parameters[0].Type, lambda.ReturnType);

        return (BsonExpression)method.Invoke(mapper, new object[] { lambda });
    }
}

internal static class LiteDbXQueryExpressionHelper
{
    public static LambdaExpression GetLambda(Expression expression)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));

        var unwrapped = StripQuotes(expression);

        if (unwrapped is LambdaExpression lambda)
        {
            return lambda;
        }

        throw new NotSupportedException($"Expected a quoted lambda expression but found {expression.NodeType} ({expression}).");
    }

    public static Expression StripQuotes(Expression expression)
    {
        while (expression is UnaryExpression unary && expression.NodeType == ExpressionType.Quote)
        {
            expression = unary.Operand;
        }

        return expression;
    }

    public static T Evaluate<T>(Expression expression)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));

        var value = Evaluate(expression);

        if (value == null)
        {
            return default;
        }

        if (value is T typed)
        {
            return typed;
        }

        return (T)Convert.ChangeType(value, typeof(T));
    }

    public static object Evaluate(Expression expression)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));

        if (expression is ConstantExpression constant)
        {
            return constant.Value;
        }

        var objectValue = Expression.Convert(expression, typeof(object));
        var lambda = Expression.Lambda<Func<object>>(objectValue);

        return lambda.Compile().Invoke();
    }

    public static Type GetSequenceElementType(Type sequenceType)
    {
        if (sequenceType == null) return null;

        if (sequenceType.IsGenericType)
        {
            var genericDefinition = sequenceType.GetGenericTypeDefinition();
            if (genericDefinition == typeof(IQueryable<>) || genericDefinition == typeof(IOrderedQueryable<>))
            {
                return sequenceType.GetGenericArguments()[0];
            }
        }

        var queryableInterface = sequenceType
            .GetInterfaces()
            .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IQueryable<>));

        return queryableInterface?.GetGenericArguments()[0];
    }
}

internal static class LiteDbXQueryableDebugExtensions
{
    public static LiteDbXQueryState ToQueryState(this IQueryable source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        return GetProvider(source).Translate(source.Expression);
    }

    public static Query ToQuery(this IQueryable source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        return GetProvider(source).LowerToQuery(source.Expression);
    }

    public static LiteQueryable<T> ToNativeQueryable<T>(this IQueryable<T> source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        var provider = GetProvider(source);
        var state = provider.Translate(source.Expression);
        var query = provider.LowerToQuery(state);

        return new LiteQueryable<T>(state.Root.Engine, state.Root.Mapper, state.Root.CollectionName, query, state.Root.Transaction);
    }

    private static LiteDbXQueryProvider GetProvider(IQueryable source)
    {
        if (source.Provider is LiteDbXQueryProvider provider)
        {
            return provider;
        }

        throw new ArgumentException("The supplied IQueryable is not backed by the LiteDbX LINQ provider.", nameof(source));
    }
}

