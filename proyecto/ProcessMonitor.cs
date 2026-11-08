using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace _stick;

internal sealed class ProcessMonitor
{
    private readonly int sessionId;

    public ProcessMonitor()
    {
        using var current = Process.GetCurrentProcess();
        sessionId = current.SessionId;
    }

    public Dictionary<string, HashSet<int>> FindRunning(IEnumerable<string> executables)
    {
        var watchedPaths = executables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var runningPaths = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in watchedPaths.Select(Path.GetFileNameWithoutExtension)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        if (process.SessionId != sessionId || process.MainModule?.FileName is not string path
                            || !watchedPaths.Contains(path))
                            continue;

                        if (!runningPaths.TryGetValue(path, out var ids))
                        {
                            ids = [];
                            runningPaths.Add(path, ids);
                        }
                        ids.Add(process.Id);
                    }
                    catch (Exception error) when (error is Win32Exception or InvalidOperationException)
                    {
                        // The process may have exited or Windows may deny access to its executable.
                    }
                }
            }
        }

        return runningPaths;
    }
}
