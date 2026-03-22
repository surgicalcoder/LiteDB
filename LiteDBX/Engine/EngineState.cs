using System;
using System.IO;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

internal class EngineState
{
    private readonly LiteEngine _engine; // can be null for unit tests
    private readonly EngineSettings _settings;
    private Exception _exception;
    public bool Disposed = false;

    public EngineState(LiteEngine engine, EngineSettings settings)
    {
        _engine = engine;
        _settings = settings;
    }

    public void Validate()
    {
        if (Disposed)
        {
            throw _exception ?? LiteException.EngineDisposed();
        }
    }

    public bool Handle(Exception ex)
    {
        LOG(ex.Message, "ERROR");

        if (ex is IOException ||
            (ex is LiteException lex && lex.ErrorCode == LiteException.INVALID_DATAFILE_STATE))
        {
            _exception = ex;

            _engine?.Close(ex);

            return false;
        }

        return true;
    }

    public BsonValue ReadTransform(string collection, BsonValue value)
    {
        if (_settings?.ReadTransform is null)
        {
            return value;
        }

        return _settings.ReadTransform(collection, value);
    }

#if DEBUG
    public Action<PageBuffer> SimulateDiskReadFail = null;
    public Action<PageBuffer> SimulateDiskWriteFail = null;
#endif
}