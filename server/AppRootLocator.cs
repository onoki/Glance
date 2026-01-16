using System.IO;

namespace Glance.Server;

public static class AppRootLocator
{
    public static string Find(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current != null)
        {
            var docsPath = Path.Combine(current.FullName, "docs", "architecture.md");
            if (File.Exists(docsPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return startDirectory;
    }
}
