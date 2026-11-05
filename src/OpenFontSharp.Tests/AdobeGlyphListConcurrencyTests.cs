using System.Reflection;

namespace OpenFontSharp.Tests;

public class AdobeGlyphListConcurrencyTests
{
    [Fact]
    public void ConcurrentFirstUse_ReturnsConsistentResults()
    {
        var type = typeof(Typeface).Assembly.GetType("OpenFontSharp.AdditionalInfo.AdobeGlyphList")!;
        const BindingFlags F = BindingFlags.Static | BindingFlags.NonPublic;
        var get = type.GetMethod("GetUnicodeValueByGlyphName", BindingFlags.Static | BindingFlags.Public)!;

        for (int round = 0; round < 20; round++)
        {
            // Reset the lazy state so each round exercises first use again.
            type.GetField("s_glyphNameToUnicodeScalarValueDic", F)!.SetValue(null, new Dictionary<string, int>());
            type.GetField("s_unicodeScalarValueToGlyphNameDic", F)!.SetValue(null, new Dictionary<int, string>());
            type.GetField("s_init", F)!.SetValue(null, false);

            var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();
            Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
            {
                try
                {
                    ((int)get.Invoke(null, new object[] { "A" })!).Should().Be(0x41);
                    ((int)get.Invoke(null, new object[] { "AE" })!).Should().Be(0xC6);
                }
                catch (Exception ex) { failures.Add(ex); }
            });
            failures.Should().BeEmpty();
        }
    }
}
