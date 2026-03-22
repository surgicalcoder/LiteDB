namespace LiteDbX.Engine;

internal enum TransactionState
{
    Active,
    Committed,
    Aborted,
    Disposed
}