using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.QueryTest;

public class GroupBy_Tests
{
    [Fact]
    public async Task Queryable_GroupBy_Age_With_Count()
    {
        await using var db = await PersonGroupByData.CreateAsync();
        var (collection, local) = db.GetData();

        var queryable = collection.AsQueryable()
            .GroupBy(x => x.Age)
            .Select(g => new { Age = g.Key, Count = g.Count() });

        var lowered = queryable.ToQuery();

        var expectedLocal = local
            .GroupBy(x => x.Age)
            .OrderBy(x => x.Key)
            .Select(x => new { Age = x.Key, Count = x.Count() })
            .ToArray();

        var expectedNative = await collection.Query()
            .GroupBy(lowered.GroupBy)
            .Select(lowered.Select)
            .ToArray();

        var actual = await queryable.ToArrayAsync();

        lowered.GroupBy.Should().NotBeNull();
        lowered.Having.Should().BeNull();
        lowered.Select.Source.Should().Contain("@key");
        lowered.Select.Source.Should().Contain("COUNT(");

        actual.Should().Equal(expectedLocal);
        actual.Select(x => x.Age).Should().Equal(expectedNative.Select(x => x["Age"].AsInt32));
        actual.Select(x => x.Count).Should().Equal(expectedNative.Select(x => x["Count"].AsInt32));
    }

    [Fact]
    public async Task Queryable_GroupBy_Year_With_Sum_Age()
    {
        await using var db = await PersonGroupByData.CreateAsync();
        var (collection, local) = db.GetData();

        var queryable = collection.AsQueryable()
            .GroupBy(x => x.Date.Year)
            .Select(g => new { Year = g.Key, Sum = g.Sum(x => x.Age) });

        var lowered = queryable.ToQuery();

        var expectedLocal = local
            .GroupBy(x => x.Date.Year)
            .OrderBy(x => x.Key)
            .Select(x => new { Year = x.Key, Sum = x.Sum(q => q.Age) })
            .ToArray();

        var expectedNative = await collection.Query()
            .GroupBy(lowered.GroupBy)
            .Select(lowered.Select)
            .ToArray();

        var actual = await queryable.ToArrayAsync();

        lowered.GroupBy.Should().NotBeNull();
        lowered.Select.Source.Should().Contain("SUM(");

        actual.Should().Equal(expectedLocal);
        actual.Select(x => x.Year).Should().Equal(expectedNative.Select(x => x["Year"].AsInt32));
        actual.Select(x => x.Sum).Should().Equal(expectedNative.Select(x => x["Sum"].AsInt32));
    }

    [Fact]
    public async Task Queryable_GroupBy_Age_With_Having_Count_Filter()
    {
        await using var db = await PersonGroupByData.CreateAsync();
        var (collection, local) = db.GetData();

        var queryable = collection.AsQueryable()
            .GroupBy(x => x.Age)
            .Where(g => g.Count() >= 2 && g.Key >= 30)
            .Select(g => new { Age = g.Key, Count = g.Count() });

        var lowered = queryable.ToQuery();

        var expectedLocal = local
            .GroupBy(x => x.Age)
            .Where(x => x.Count() >= 2 && x.Key >= 30)
            .OrderBy(x => x.Key)
            .Select(x => new { Age = x.Key, Count = x.Count() })
            .ToArray();

        var native = collection.Query().GroupBy(lowered.GroupBy).Having(lowered.Having).Select(lowered.Select);
        var expectedNative = await native.ToArray();
        var actual = await queryable.ToArrayAsync();

        lowered.Having.Should().NotBeNull();
        lowered.Having.Source.Should().Contain("COUNT(");
        lowered.Having.Source.Should().Contain("@key");

        actual.Should().Equal(expectedLocal);
        actual.Select(x => x.Age).Should().Equal(expectedNative.Select(x => x["Age"].AsInt32));
        actual.Select(x => x.Count).Should().Equal(expectedNative.Select(x => x["Count"].AsInt32));
    }

    [Fact]
    public async Task Queryable_GroupBy_Without_Projection_Fails_Clearly()
    {
        await using var db = await PersonGroupByData.CreateAsync();
        var (collection, _) = db.GetData();

        Func<Task> act = async () => _ = await collection.AsQueryable()
            .GroupBy(x => x.Age)
            .ToListAsync();

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Raw GroupBy*grouped aggregate projections*Query()*");
    }

    [Fact]
    public async Task Queryable_GroupBy_With_Array_Aggregation_Fails_Clearly()
    {
        await using var db = await PersonGroupByData.CreateAsync();
        var (collection, _) = db.GetData();

        Func<Task> act = async () => _ = await collection.AsQueryable()
            .GroupBy(x => x.Email.Substring(x.Email.IndexOf("@") + 1))
            .Select(g => new
            {
                Domain = g.Key,
                Users = g.Select(x => new { x.Name, x.Age }).ToArray()
            })
            .ToListAsync();

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*grouped projection shape*collection.Query()*");
    }
}