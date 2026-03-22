using System;

namespace LiteDbX.Stress;

public interface ITestItem
{
    string Name { get; }
    int TaskCount { get; }
    TimeSpan Sleep { get; }
    BsonValue Execute(LiteDatabase db);
}