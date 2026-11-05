using System.Reflection;

namespace OpenFontSharp.Tests;

public class GsubUnsupportedLookupTests
{
    [Fact]
    public void UnsupportedLookupStubsThrowFontFormatExceptions()
    {
        const BindingFlags nestedFlags = BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags methodFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var assembly = typeof(Typeface).Assembly;
        var gsub = assembly.GetType("OpenFontSharp.Tables.AdvancedLayout.GSUB")!;
        var lookup = gsub.GetNestedType("LookupTable", nestedFlags)!;
        var context1 = lookup.GetNestedType("LkSubTableT5Fmt1", nestedFlags)!;
        var context3 = lookup.GetNestedType("LkSubTableT5Fmt3", nestedFlags)!;

        var context1Instance = Activator.CreateInstance(context1, nonPublic: true)!;
        var context1Collect = context1.GetMethod("CollectAssociatedSubtitutionGlyphs", methodFlags)!;
        AssertUnsupported(() => context1Collect.Invoke(context1Instance, [new List<ushort>()]));

        var context3Instance = Activator.CreateInstance(context3, nonPublic: true)!;
        var context3Collect = context3.GetMethod("CollectAssociatedSubtitutionGlyphs", methodFlags)!;
        var context3Substitute = context3.GetMethod("DoSubstitutionAt", methodFlags)!;
        AssertUnsupported(() => context3Collect.Invoke(context3Instance, [new List<ushort>()]));
        AssertUnsupported(() => context3Substitute.Invoke(context3Instance, [null, 0, 0]));

        var readType8 = lookup.GetMethod("ReadLookupType8", methodFlags)!;
        using var reader = new BinaryReader(new MemoryStream());
        AssertUnsupported(() => readType8.Invoke(null, [reader, 0L]));
    }

    private static void AssertUnsupported(Action action)
    {
        Record.Exception(action).Should().BeOfType<TargetInvocationException>()
            .Which.InnerException.Should().BeOfType<OpenFontNotSupportedException>();
    }
}
