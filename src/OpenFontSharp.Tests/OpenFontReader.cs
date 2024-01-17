namespace OpenFontSharp.Tests
{
  public class Tests
  {
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void Test1()
    {

      OpenFontReader reader = new OpenFontReader();
      reader.Read("../../../TestFonts/SourceSansPro-Regular.otf");

      Assert.Pass();
    }
  }
}