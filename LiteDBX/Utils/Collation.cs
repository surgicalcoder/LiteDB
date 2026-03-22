using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteDbX;

/// <summary>
/// Implement how database will compare to order by/find strings according defined culture/compare options
/// If not set, default is CurrentCulture with IgnoreCase
/// </summary>
public class Collation : IComparer<BsonValue>, IComparer<string>, IEqualityComparer<BsonValue>
{
    public static Collation Default = new(LiteDbX.LCID.Current, CompareOptions.IgnoreCase);

    public static Collation Binary = new(127 /* Invariant */, CompareOptions.Ordinal);
    private readonly CompareInfo _compareInfo;

    public Collation(string collation)
    {
        var parts = collation.Split('/');
        var culture = parts[0];
        var sortOptions = parts.Length > 1 ? (CompareOptions)Enum.Parse(typeof(CompareOptions), parts[1]) : CompareOptions.None;

        LCID = LiteDbX.LCID.GetLCID(culture);
        SortOptions = sortOptions;
        Culture = new CultureInfo(culture);

        _compareInfo = Culture.CompareInfo;
    }

    public Collation(int lcid, CompareOptions sortOptions)
    {
        LCID = lcid;
        SortOptions = sortOptions;
        Culture = LiteDbX.LCID.GetCulture(lcid);

        _compareInfo = Culture.CompareInfo;
    }

    /// <summary>
    /// Get LCID code from culture
    /// </summary>
    public int LCID { get; }

    /// <summary>
    /// Get database language culture
    /// </summary>
    public CultureInfo Culture { get; }

    /// <summary>
    /// Get options to how string should be compared in sort
    /// </summary>
    public CompareOptions SortOptions { get; }

    public int Compare(BsonValue left, BsonValue rigth)
    {
        return left.CompareTo(rigth, this);
    }

    /// <summary>
    /// Compare 2 string values using current culture/compare options
    /// </summary>
    public int Compare(string left, string right)
    {
        var result = _compareInfo.Compare(left, right, SortOptions);

        return result < 0 ? -1 : result > 0 ? +1 : 0;
    }

    public bool Equals(BsonValue x, BsonValue y)
    {
        return Compare(x, y) == 0;
    }

    public int GetHashCode(BsonValue obj)
    {
        return obj.GetHashCode();
    }

    public override string ToString()
    {
        return Culture.Name + "/" + SortOptions;
    }
}