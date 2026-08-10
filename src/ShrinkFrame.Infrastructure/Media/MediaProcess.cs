using System.Diagnostics;

namespace ShrinkFrame.Infrastructure.Media;

internal static class MediaProcess
{
    public static Process Start(string executable, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException($"Unable to start media tool '{Path.GetFileName(executable)}'.");
        return process;
    }

    public static void KillTree(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }
}
