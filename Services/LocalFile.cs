using System.IO;
using System.Text;

namespace DynamicIslandWin.Services;

internal static class LocalFile
{
    internal static void Write(string path, string value, Encoding? encoding = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (encoding == null) File.WriteAllText(temporary, value);
            else File.WriteAllText(temporary, value, encoding);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
