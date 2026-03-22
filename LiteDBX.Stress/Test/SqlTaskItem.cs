using System;
using System.Linq;
using System.Xml;

namespace LiteDbX.Stress;

public class SqlTaskItem : ITestItem
{
    public SqlTaskItem(XmlElement el)
    {
        Name = string.IsNullOrEmpty(el.GetAttribute("name")) ? el.InnerText.Split(' ').First() : el.GetAttribute("name");
        TaskCount = string.IsNullOrEmpty(el.GetAttribute("tasks")) ? 1 : int.Parse(el.GetAttribute("tasks"));
        Sleep = TimeSpanEx.Parse(el.GetAttribute("sleep"));
        Sql = el.InnerText;
    }

    public string Sql { get; }
    public string Name { get; }
    public int TaskCount { get; }
    public TimeSpan Sleep { get; }

    public BsonValue Execute(LiteDatabase db)
    {
        using (var reader = db.Execute(Sql))
        {
            return reader.FirstOrDefault();
        }
    }
}