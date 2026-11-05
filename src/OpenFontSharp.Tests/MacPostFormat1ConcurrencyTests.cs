using System.Reflection;

namespace OpenFontSharp.Tests;

public class MacPostFormat1ConcurrencyTests
{
    [Fact]
    public void ConcurrentFirstUse_ReturnsFullyInitializedGlyphNames()
    {
        var type = typeof(Typeface).Assembly.GetType("OpenFontSharp.AdditionalInfo.MacPostFormat1")!;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var getNames = type.GetMethod("GetStdMacGlyphNames", BindingFlags.Static | BindingFlags.Public)!;
        var namesField = type.GetField("s_stdMacGlyphNames", flags)!;

        for (int round = 0; round < 20; round++)
        {
            namesField.SetValue(null, null);
            var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();
            Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
            {
                try
                {
                    var names = (string[])getNames.Invoke(null, null)!;
                    names.Should().HaveCount(260);
                    names.Take(258).Should().OnlyContain(name => !string.IsNullOrEmpty(name));
                    names[0].Should().Be(".notdef");
                    names[257].Should().Be("dcroat");
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            });

            failures.Should().BeEmpty();
        }
    }
}
