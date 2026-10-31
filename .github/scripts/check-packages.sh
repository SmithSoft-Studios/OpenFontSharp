#!/usr/bin/env bash
# Packs OpenFontSharp and proves the package works outside this repository: a new, empty project installs it from the
# freshly built package, reads a font, subsets it and reads the subset back. This exercises the subsetting API, which
# a package built from the wrong sources would lack.
# Run from the repository root (needs the .NET 10 SDK and network access to nuget.org):
#   bash .github/scripts/check-packages.sh [folder for the package]
set -euo pipefail

root=$(pwd)
out=${1:-$(mktemp -d)}
mkdir -p "$out"
out=$(cd "$out" && pwd)
work=$(mktemp -d)
winpath() { if command -v cygpath > /dev/null; then cygpath -m "$1"; else printf "%s" "$1"; fi; } # dotnet needs Windows paths under Git Bash
export NUGET_PACKAGES="$(winpath "$work")/nuget-cache" # an empty package cache, so an older build of the same version is not used

echo "== Pack"
dotnet pack src/OpenFontSharp/OpenFontSharp.csproj --configuration Release --output "$out" -p:ContinuousIntegrationBuild=true > /dev/null
nupkg=$(ls "$out"/OpenFontSharp.[0-9]*.nupkg)
version=$(basename "$nupkg" .nupkg)
version=${version#OpenFontSharp.}
font="$root/src/OpenFontSharp.Tests/Resources/Fonts/TTF/Roboto/Roboto-Regular.ttf"

echo "== Install into a clean project"
mkdir "$work/Clean" && cd "$work/Clean"
dotnet new console --framework net10.0 > /dev/null
cat > nuget.config <<CFG
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$(winpath "$out")" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
CFG
dotnet add package OpenFontSharp --version "$version" > /dev/null
cat > Program.cs <<'CS'
using OpenFontSharp;
using OpenFontSharp.Subsetting;
using System.IO.Compression;
using System.Xml.Linq;

using (var package = ZipFile.OpenRead(args[1]))
{
    var entry = package.GetEntry("lib/net10.0/OpenFontSharp.xml")
        ?? throw new Exception("Package is missing XML API documentation");
    using var xml = entry.Open();
    var document = XDocument.Load(xml);
    if (!document.Descendants("member").Any(m => (string?)m.Attribute("name") == "T:OpenFontSharp.OpenFontReader"))
        throw new Exception("Package XML does not document OpenFontReader");
}

var bytes = File.ReadAllBytes(args[0]);
Typeface typeface;
using (var ms = new MemoryStream(bytes)) typeface = new OpenFontReader().Read(ms) ?? throw new Exception("Font did not load");
var ids = new HashSet<ushort>();
foreach (var c in "Hello") ids.Add(typeface.GetGlyphIndex(c));
var result = FontSubsetter.Subset(bytes, ids);
if (result.FontData.Length >= bytes.Length) throw new Exception("Subset is not smaller than the original");
if (result.RetainedGlyphCount < ids.Count) throw new Exception("Subset lost glyphs");
using var back = new MemoryStream(result.FontData);
var subset = new OpenFontReader().Read(back) ?? throw new Exception("Subset font does not load");
if (subset.GlyphCount != result.RetainedGlyphCount) throw new Exception("Subset glyph count mismatch");
CS
dotnet run --configuration Release -- "$(winpath "$font")" "$(winpath "$nupkg")" > /dev/null
echo "   OpenFontSharp $version: includes XML API docs, installs, reads a font, subsets it and reads the subset back"

rm -rf "$work"
echo "Package in $out"
