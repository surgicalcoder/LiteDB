using System;
using System.Globalization;
using FluentAssertions;
using System.Threading.Tasks;
using LiteDbX.Engine;
using Xunit;

namespace LiteDbX.Tests.Database;

public class Database_Pragmas_Tests
{
    [Fact]
    public async Task Database_Pragmas_Get_Set()
    {
        await using var db = await LiteDatabase.Open(":memory:");

        TimeSpan.FromSeconds((await db.Pragma(Pragmas.TIMEOUT)).AsInt32).TotalSeconds.Should().Be(60.0);
        (await db.Pragma(Pragmas.UTC_DATE)).AsBoolean.Should().Be(false);
        new Collation((await db.Pragma(Pragmas.COLLATION)).AsString).SortOptions.Should().Be(CompareOptions.IgnoreCase);
        (await db.Pragma(Pragmas.LIMIT_SIZE)).AsInt64.Should().Be(long.MaxValue);
        (await db.Pragma(Pragmas.USER_VERSION)).AsInt32.Should().Be(0);
        (await db.Pragma(Pragmas.CHECKPOINT)).AsInt32.Should().Be(1000);

        // changing values
        await db.Pragma(Pragmas.TIMEOUT, (int)TimeSpan.FromSeconds(30).TotalSeconds);
        await db.Pragma(Pragmas.UTC_DATE, true);
        await db.Pragma(Pragmas.LIMIT_SIZE, 1024 * 1024);
        await db.Pragma(Pragmas.USER_VERSION, 99);
        await db.Pragma(Pragmas.CHECKPOINT, 0);

        // testing again
        TimeSpan.FromSeconds((await db.Pragma(Pragmas.TIMEOUT)).AsInt32).TotalSeconds.Should().Be(30);
        (await db.Pragma(Pragmas.UTC_DATE)).AsBoolean.Should().Be(true);
        (await db.Pragma(Pragmas.LIMIT_SIZE)).AsInt64.Should().Be(1024 * 1024);
        (await db.Pragma(Pragmas.USER_VERSION)).AsInt32.Should().Be(99);
        (await db.Pragma(Pragmas.CHECKPOINT)).AsInt32.Should().Be(0);
    }
}