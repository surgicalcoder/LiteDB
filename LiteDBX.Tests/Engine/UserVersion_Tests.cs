using FluentAssertions;
using System.Threading.Tasks;
using LiteDbX.Engine;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class UserVersion_Tests
{
    [Fact]
    public async Task UserVersion_Get_Set()
    {
        using var file = new TempFile();

        await using (var db = await LiteDatabase.Open(file.Filename))
        {
            (await db.Pragma(Pragmas.USER_VERSION)).AsInt32.Should().Be(0);
            await db.Pragma(Pragmas.USER_VERSION, 5);
            await db.Checkpoint();
        }

        await using (var db = await LiteDatabase.Open(file.Filename))
        {
            (await db.Pragma(Pragmas.USER_VERSION)).AsInt32.Should().Be(5);
        }
    }
}