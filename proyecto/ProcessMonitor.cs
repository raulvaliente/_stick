using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace _stick;

internal sealed record RunningApplication(string Name, string Executable);

internal sealed class ProcessMonitor
{
    private readonly int sessionId;

    public ProcessMonitor()
    {
        using var current = Process.GetCurrentProcess();
        sessionId = current.SessionId;
    }

    public RunningApplication[] FindOpenApplications()
    {
        var applications = new Dictionary<string, RunningApplication>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.SessionId != sessionId
                        || process.MainWindowHandle == IntPtr.Zero)
                        continue;

                    var module = process.MainModule;
                    if (module?.FileName is not string path || applications.ContainsKey(path)
                        || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var metadata = module.FileVersionInfo;
                    var name = metadata.FileDescription?.Trim();
                    var productName = metadata.ProductName?.Trim();
                    if (string.IsNullOrEmpty(name) || (!string.IsNullOrEmpty(productName)
                        && name.StartsWith(productName + " (", StringComparison.OrdinalIgnoreCase)))
                        name = productName;

                    applications.Add(path, new RunningApplication(
                        string.IsNullOrEmpty(name) ? Path.GetFileNameWithoutExtension(path) : name, path));
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException)
                {
                    // A window can close while the process or its metadata is being inspected.
                }
            }
        }

        return applications.Values.OrderBy(application => application.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(application => application.Executable, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
