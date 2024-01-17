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
    public void ReadPreview_TrueTypeFont_Roboto_Should_Return_PreviewFontInfo()
    {
      //init open font reader
      var reader = new OpenFontReader();

      //get roboto fonts
      var ttfFonts = _getAllFilePaths("../../../Resources/Fonts/TTF/Roboto", "*.ttf");

      var lstFontInfo = new List<PreviewFontInfo>();
      foreach (var font in ttfFonts)
      {
        var stream = new MemoryStream(File.ReadAllBytes(font));
        lstFontInfo.Add(reader.ReadPreview(stream));
      }

      Assert.That(lstFontInfo.Count, Is.EqualTo(12));
      var robotoItalic = lstFontInfo.FirstOrDefault(x => x.Name == "Roboto" && x.SubFamilyName == "Italic");
      Assert.That(robotoItalic, Has.Property("PostScriptName"));
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
        var info = reader.Read(stream);
        Console.WriteLine($"Name:{info.Name},Style:{info.FontSubFamily}");
        lstFontInfo.Add(info);
      }

      Assert.That(lstFontInfo.Count, Is.EqualTo(12));
      var robotoItalic = lstFontInfo.FirstOrDefault(x => x.Name == "Roboto" && x.FontSubFamily == "Italic");
      Assert.That(robotoItalic, Has.Property("PostScriptName"));
    }

    [Test]
    public void Read_OpenTypeFont_GreatVibes_Should_Return_Typeface_Info()
    {
      //init open font reader
      var reader = new OpenFontReader();

      //get roboto fonts
      var otfFonts = _getAllFilePaths("../../../Resources/Fonts/OTF/great-vibes", "*.otf");

      var lstFontInfo = new List<Typeface>();
      foreach (var font in otfFonts)
      {
        var stream = new MemoryStream(File.ReadAllBytes(font));
        lstFontInfo.Add(reader.Read(stream));
      }

      Assert.That(lstFontInfo.Count, Is.EqualTo(1));
      var greatvibes = lstFontInfo.FirstOrDefault();
      
      Assert.That(greatvibes, Has.Property("PostScriptName"));
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