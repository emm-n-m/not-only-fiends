using System.Text;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests;

public class EnvFileTests
{
    [Theory]
    [InlineData("CHARACTERS_PATH='/mnt/c/Users/emaur/Personal Projects/characters/'", "/mnt/c/Users/emaur/Personal Projects/characters/")]
    [InlineData("SOURCE_PDFS_PATH=\"/mnt/c/Users/emaur/Personal Projects/sources\"", "/mnt/c/Users/emaur/Personal Projects/sources")]
    [InlineData("EXTRA_PACKS_PATH=/mnt/c/Users/emaur/private-packs", "/mnt/c/Users/emaur/private-packs")]
    public void Load_UnquotesValues(string line, string expected)
    {
        var path = Path.Combine(Path.GetTempPath(), $"not-only-fiends-env-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(path, line, Encoding.UTF8);

            var values = EnvFile.Load(path);

            Assert.Equal(expected, values.Single().Value);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
