using System.Diagnostics;
using System.Threading.Tasks;
using LiteDbX.Utils.Extensions;
using Xunit;

namespace LiteDbX.Tests.Internals;

public class Extensions_Test
{
    // Asserts that chained IEnumerable<T>.OnDispose(()=> { }) calls the action on dispose, even when chained
    [Fact]
    public void EnumerableExtensions_OnDispose()
    {
        var disposed = false;
        var disposed1 = false;
        var enumerable = new[] { 1, 2, 3 }.OnDispose(() => disposed = true).OnDispose(() => disposed1 = true);

        foreach (var item in enumerable)
        {
            // do nothing
        }

        Assert.True(disposed);
        Assert.True(disposed1);
    }

    // tests IDisposable StartDisposable(this Stopwatch stopwatch)
    [Fact]
    public async Task StopWatchExtensions_StartDisposable()
    {
        var stopwatch = new Stopwatch();

        using (stopwatch.StartDisposable())
        {
            await Task.Delay(100);
        }

        Assert.True(stopwatch.ElapsedMilliseconds > 0);
    }
}