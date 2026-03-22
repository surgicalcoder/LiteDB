namespace LiteDbX.Engine;

/// <summary>
/// Simple parameter class to be passed into IEnumerable classes loop ("ref" do not works)
/// </summary>
internal class Done
{
    public int Count = 0;
    public bool Running = false;
}