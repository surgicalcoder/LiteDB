using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Engine;

public partial class LiteEngine
{
    /// <summary>
    /// Recover a corrupt datafile using the async rebuild process.
    /// Used by the explicit <c>LiteEngine.Open(...)</c> lifecycle.
    /// </summary>
    private async ValueTask Recovery(Collation collation, CancellationToken cancellationToken)
    {
        var rebuilder = new RebuildService(_settings);
        var options = new RebuildOptions
        {
            Collation = collation,
            Password = _settings.Password,
            IncludeErrorReport = true
        };

        await rebuilder.RebuildAsync(options, cancellationToken).ConfigureAwait(false);
    }

}