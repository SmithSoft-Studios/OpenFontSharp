using System.Diagnostics;

namespace OpenFontSharp.Tests;

public class LibraryQualityTests
{
    [Fact]
    public async Task ReleaseLibraryBuild_HasNoUninitializedMembersOrMissingXmlDocumentation()
    {
        var project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "OpenFontSharp", "OpenFontSharp.csproj"));
        var buildDirectory = Path.Combine(Path.GetTempPath(), "OpenFontSharp-quality-" + Guid.NewGuid().ToString("N"));
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
        {
            "build", project, "-c", "Release", "--no-restore", "--no-incremental",
            "-p:GenerateDocumentationFile=true", "-warnaserror:CS8618,CS1591",
            "-p:GeneratePackageOnBuild=false", "-v:minimal",
            "-p:IntermediateOutputPath=" + Path.Combine(buildDirectory, "obj") + Path.DirectorySeparatorChar,
            "-p:OutputPath=" + Path.Combine(buildDirectory, "bin") + Path.DirectorySeparatorChar
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        Directory.CreateDirectory(buildDirectory);
        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the library quality build.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw;
            }

            var output = await outputTask + await errorTask;
            Assert.True(process.ExitCode == 0, output);
            Assert.DoesNotContain(": warning CS8618:", output);
            Assert.DoesNotContain(": warning CS1591:", output);
            Assert.DoesNotContain(": error CS8618:", output);
            Assert.DoesNotContain(": error CS1591:", output);
            Assert.True(File.Exists(Path.Combine(buildDirectory, "bin", "OpenFontSharp.xml")),
                "The library must ship its XML API documentation.");
        }
        finally
        {
            Directory.Delete(buildDirectory, recursive: true);
        }
    }
}
