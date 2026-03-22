namespace LiteDbX.Engine;

/// <summary>
/// Represent an GroupBy definition (is based on OrderByDefinition)
/// </summary>
internal class GroupBy
{
    public GroupBy(BsonExpression expression, BsonExpression select, BsonExpression having)
    {
        Expression = expression;
        Select = select;
        Having = having;
    }

    public BsonExpression Expression { get; }

    public BsonExpression Select { get; }

    public BsonExpression Having { get; }
}