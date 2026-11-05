#if DEBUG
using System.Reflection;

namespace OpenFontSharp.Tests;

public class DebugTypefaceIdConcurrencyTests
{
    [Fact]
    public void ConcurrentConstructionAssignsUniqueDebugIds()
    {
        var idField = typeof(Typeface).GetField("dbugId", BindingFlags.Instance | BindingFlags.Public)!;
        var ids = new System.Collections.Concurrent.ConcurrentBag<int>();

        Parallel.For(0, 256, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
        {
            var typeface = Activator.CreateInstance(typeof(Typeface), nonPublic: true)!;
            ids.Add((int)idField.GetValue(typeface)!);
        });

        ids.Should().HaveCount(256).And.OnlyHaveUniqueItems();
    }
}
#endif
