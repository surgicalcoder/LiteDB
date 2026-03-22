namespace LiteDbX.Shell;

internal interface IShellCommand
{
    bool IsCommand(StringScanner s);

    void Execute(StringScanner s, Env env);
}