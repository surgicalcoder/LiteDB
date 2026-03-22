using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace LiteDbX.Utils;

internal class TryCatch
{
    public readonly List<Exception> Exceptions = new();

    public TryCatch() { }

    public TryCatch(Exception initial)
    {
        Exceptions.Add(initial);
    }

    public bool InvalidDatafileState => Exceptions.Any(ex =>
        ex is LiteException liteEx &&
        liteEx.ErrorCode == LiteException.INVALID_DATAFILE_STATE);

    [DebuggerHidden]
    public void Catch(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Exceptions.Add(ex);
        }
    }

    /// <summary>
    /// Awaitable catch for use on engine async-close paths.
    /// </summary>
    [DebuggerHidden]
    public async Task CatchAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Exceptions.Add(ex);
        }
    }
}