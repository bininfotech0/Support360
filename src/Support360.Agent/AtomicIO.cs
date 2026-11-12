using System.Text;

namespace Support360.Agent;

internal static class AtomicIO
{
    public static void WriteFileAtomic(string path, string content, UnixFileMode fileMode)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var payload = Encoding.UTF8.GetBytes(content);
            stream.Write(payload, 0, payload.Length);
        }
        ApplyUnixFileMode(temp, fileMode);
        File.Move(temp, path, overwrite: true);
    }

    public static void CreateDirectory(string path, UnixFileMode directoryMode)
    {
        var created = Directory.CreateDirectory(path);
        ApplyUnixFileMode(created.FullName, directoryMode);
    }

    private static void ApplyUnixFileMode(string path, UnixFileMode mode)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, mode);
        }
    }
}
