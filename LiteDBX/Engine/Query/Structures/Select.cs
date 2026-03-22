namespace LiteDbX.Engine;

/// <summary>
/// Represent a Select expression
/// </summary>
internal class Select
{
    public Select(BsonExpression expression, bool all)
    {
        Expression = expression;
        All = all;
    }

    public BsonExpression Expression { get; }

    public bool All { get; }
}