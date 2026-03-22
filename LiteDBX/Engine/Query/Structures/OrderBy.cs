namespace LiteDbX.Engine;

/// <summary>
/// Represent an OrderBy definition
/// </summary>
internal class OrderBy
{
    public OrderBy(BsonExpression expression, int order)
    {
        Expression = expression;
        Order = order;
    }

    public BsonExpression Expression { get; }

    public int Order { get; set; }
}