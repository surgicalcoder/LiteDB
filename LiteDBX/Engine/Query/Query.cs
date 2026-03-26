using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LiteDbX;

/// <summary>
/// Represent full query options
/// </summary>
public partial class Query
{
    public BsonExpression Select { get; set; } = BsonExpression.Root;

    public List<BsonExpression> Includes { get; } = new();
    public List<BsonExpression> Where { get; } = new();

    public List<QueryOrder> OrderBy { get; } = new();

    public BsonExpression GroupBy { get; set; } = null;
    public BsonExpression Having { get; set; } = null;

    public int Offset { get; set; } = 0;
    public int Limit { get; set; } = int.MaxValue;
    public bool ForUpdate { get; set; } = false;

    public string Into { get; set; }
    public BsonAutoId IntoAutoId { get; set; } = BsonAutoId.ObjectId;

    public bool ExplainPlan { get; set; }

    /// <summary>
    /// [ EXPLAIN ]
    /// SELECT {selectExpr}
    /// [ INTO {newcollection|$function} [ : {autoId} ] ]
    /// [ FROM {collection|$function} ]
    /// [ INCLUDE {pathExpr0} [, {pathExprN} ]
    /// [ WHERE {filterExpr} ]
    /// [ GROUP BY {groupByExpr} ]
    /// [ HAVING {filterExpr} ]
    /// [ ORDER BY {orderByExpr} [ ASC | DESC ] ]
    /// [ LIMIT {number} ]
    /// [ OFFSET {number} ]
    /// [ FOR UPDATE ]
    /// </summary>
    public string ToSQL(string collection)
    {
        var sb = new StringBuilder();

        if (ExplainPlan)
        {
            sb.AppendLine("EXPLAIN");
        }

        sb.AppendLine($"SELECT {Select.Source}");

        if (Into != null)
        {
            sb.AppendLine($"INTO {Into}:{IntoAutoId.ToString().ToLower()}");
        }

        sb.AppendLine($"FROM {collection}");

        if (Includes.Count > 0)
        {
            sb.AppendLine($"INCLUDE {string.Join(", ", Includes.Select(x => x.Source))}");
        }

        if (Where.Count > 0)
        {
            sb.AppendLine($"WHERE {string.Join(" AND ", Where.Select(x => x.Source))}");
        }

        if (GroupBy != null)
        {
            sb.AppendLine($"GROUP BY {GroupBy.Source}");
        }

        if (Having != null)
        {
            sb.AppendLine($"HAVING {Having.Source}");
        }

        if (OrderBy.Count > 0)
        {
            var orderBy = OrderBy
                .Select(x => $"{x.Expression.Source} {(x.Order == Ascending ? "ASC" : "DESC")}");

            sb.AppendLine($"ORDER BY {string.Join(", ", orderBy)}");
        }

        if (Limit != int.MaxValue)
        {
            sb.AppendLine($"LIMIT {Limit}");
        }

        if (Offset != 0)
        {
            sb.AppendLine($"OFFSET {Offset}");
        }

        if (ForUpdate)
        {
            sb.AppendLine("FOR UPDATE");
        }

        return sb.ToString().Trim();
    }
}