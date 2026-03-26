namespace LiteDbX;

/// <summary>
/// Represents a single ORDER BY segment containing the expression and direction.
/// </summary>
public class QueryOrder
{
    public QueryOrder(BsonExpression expression, int order)
    {
        Expression = expression;
        Order = order;
    }

    public BsonExpression Expression { get; }

    public int Order { get; }
}
