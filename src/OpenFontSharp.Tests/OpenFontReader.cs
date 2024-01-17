using Newtonsoft.Json;

namespace OpenFontSharp.Tests
{
  public class Tests
  {
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void Read_TrueTypeFont_Roboto_Should_Return_Typeface_Info()
    {
      //init open font reader
      var reader = new OpenFontReader();

      //get roboto fonts
      var ttfFonts = _getAllFilePaths("../../../Resources/Fonts/TTF/Roboto", "*.ttf");

      var lstFontInfo = new List<Typeface>();
      foreach (var font in ttfFonts)
      {
        var stream = new MemoryStream(File.ReadAllBytes(font));
        lstFontInfo.Add(reader.Read(stream));
      }

      Assert.That(lstFontInfo.Count, Is.EqualTo(12));
      var robotoItalic = lstFontInfo.FirstOrDefault(x => x.Name == "Roboto" && x.FontSubFamily == "Italic");
      Assert.That(robotoItalic, Has.Property("PostScriptName"));
    }


    private List<string> _getAllFilePaths(string rootDirectory,string pattern = "*.*")
    {
      var filePaths = Directory.GetFiles(rootDirectory, pattern).ToList();

      foreach (var directory in Directory.GetDirectories(rootDirectory))
      {
        filePaths.AddRange(_getAllFilePaths(directory,pattern));
      }

      return filePaths;
    }
  }
}