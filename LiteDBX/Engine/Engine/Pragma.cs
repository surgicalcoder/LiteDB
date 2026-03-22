using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Engine;

public partial class LiteEngine
{
    /// <summary>
    /// Get engine internal pragma value.
    ///
    /// Phase 2: signature updated to match <see cref="ILiteEngine.Pragma(string, CancellationToken)"/>.
    /// Phase 3 bridge: pragma read is synchronous; returns a completed <see cref="ValueTask{T}"/>.
    /// </summary>
    public ValueTask<BsonValue> Pragma(string name, CancellationToken cancellationToken = default)
    {
        return new ValueTask<BsonValue>(_header.Pragmas.Get(name));
    }

    /// <summary>
    /// Set engine pragma new value (some pragmas will be affected only after reload).
    ///
    /// Phase 2: signature updated to match <see cref="ILiteEngine.Pragma(string, BsonValue, CancellationToken)"/>.
    /// Uses <see cref="LiteTransaction.HasActive"/> instead of the removed <c>IsInTransaction</c> on
    /// <see cref="LockService"/>, and <see cref="AutoTransactionAsync{T}"/> instead of the removed
    /// synchronous <c>AutoTransaction</c>.
    /// </summary>
    public ValueTask<bool> Pragma(string name, BsonValue value, CancellationToken cancellationToken = default)
    {
        if (_header.Pragmas.Get(name) == value)
        {
            return new ValueTask<bool>(false);
        }

        if (LiteTransaction.HasActive)
        {
            throw LiteException.AlreadyExistsTransaction();
        }

        return AutoTransactionAsync((transaction, _) =>
        {
            transaction.Pages.Commit += h => { h.Pragmas.Set(name, value, true); };
            return new ValueTask<bool>(true);
        }, cancellationToken);
    }
}

