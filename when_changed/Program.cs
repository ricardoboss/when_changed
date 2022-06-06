using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Linq;

// Quick'n'Dirty Hack - based on: http://msdn.microsoft.com/en-GB/library/system.io.filesystemwatcher.changed.aspx

namespace when_changed;

internal static class Program
{
    private static string? mCommand;

    private static State mState;
    private static readonly object MStateLock = new();

    public static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: when_changed <path> <command>");

            return;
        }

        var watcher = CreateWatcher(args[0]);

        mCommand = args[1];

        // Add event handlers.
        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnRenamed;

        // Begin watching.
        watcher.EnableRaisingEvents = true;

        // Wait for the user to quit the program.
        Console.WriteLine("when_changed now watching: " + watcher.Path + watcher.Filter);
        Console.WriteLine("Ctrl-C to quit.");

        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key != ConsoleKey.F)
                continue;

            Console.WriteLine("Forcing run...");
            RunCmd("");
        }
    }

    public static FileSystemWatcher CreateWatcher(string path)
    {
        // Two things are determined from the argument:
        string dirToWatch; // The directory to watch.
        string? fileFilter; // The filter for which files in that directory to watch.

        // ./
        // dir/*
        // C:\windows
        // D:\Data\**

        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(Directory.GetCurrentDirectory(), path);
        }

        dirToWatch = Path.GetFullPath(path);
        if (path.EndsWith("/") || path.EndsWith("\\"))
        {
            fileFilter = "**";
        }
        else
        {
            fileFilter = Path.GetFileName(path);
        }

        // Create a new FileSystemWatcher and set its properties.
        return new()
        {
            Path = dirToWatch,
            Filter = fileFilter,
            IncludeSubdirectories = fileFilter.Contains("**"),

            // Watch for changes in LastAccess and LastWrite times, and the renaming of files or directories.
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
        };
    }

    private static void OnChanged(object source, FileSystemEventArgs e)
    {
        // Specify what is done when a file is changed, created, or deleted.
        Console.WriteLine(DateTime.Now.ToShortTimeString() + " File: " + e.FullPath + " " + e.ChangeType);
        RunCmd(e.FullPath);
    }

    private static void OnRenamed(object source, RenamedEventArgs e)
    {
        // Specify what is done when a file is renamed.
        Console.WriteLine(DateTime.Now.ToShortTimeString() + "File: {0} renamed to {1}.", e.OldFullPath, e.FullPath);
        RunCmd(e.FullPath);
    }

    private static void RunCmd(string changedFile)
    {
        // When a file is updated, we often get a flurry of updates in a single second.
        lock (MStateLock)
        {
            switch (mState)
            {
                case State.Executing:
                    // Oh noeeees - it changed while we were executing. do it again straight after.
                    Console.WriteLine(" -- output will be dirty - will run again soon...");
                    mState = State.ExecutingDirty;
                    break;
                case State.ExecutingDirty:
                    // Leave the flag dirty.
                    break;
                case State.WaitingToExecute:
                    break;
                case State.Watching:
                    // Start a new thread to delay and run the command, meanwhile subsequent nots. ignored.
                    mState = State.WaitingToExecute;
                    var t = new Thread(ThreadRun);
                    t.Start(changedFile);
                    break;
                default:
                    throw new InvalidProgramException("argh! enum values?!");
            }
        }
    }

    private static void ThreadRun(object changedFile)
    {
        var changedfile = (string)changedFile;
        var again = true;
        while (again)
        {
            WaitThenRun(changedfile);

            // When a file is updated, we often get a flurry of updates in a single second.
            lock (MStateLock)
            {
                switch (mState)
                {
                    case State.Executing:
                        // no subsequent changes - output ok (ish)
                        mState = State.Watching;
                        again = false;
                        break;
                    case State.ExecutingDirty:
                        // Clean the dirty flag, and repeat.
                        mState = State.WaitingToExecute;
                        again = true;
                        break;
                    case State.WaitingToExecute:
                        throw new InvalidProgramException("shouldn't happen");
                    case State.Watching:
                        throw new InvalidProgramException("shouldn't happen");
                    default:
                        throw new InvalidProgramException("argh! enum values?!");
                }
            }
        }
    }

    private static void WaitThenRun(string filechanged)
    {
        Console.WriteLine("Running the command any second now...");

        // Wait for things to calm down.
        Thread.Sleep(1500);

        var startinfo = new ProcessStartInfo
        {
            FileName = mCommand?.Split(' ')[0],
            Arguments = string.Join(" ", mCommand?.Split(' ').Select(a => a.Replace("$1", filechanged)) ?? Array.Empty<string>()),
            WindowStyle = ProcessWindowStyle.Minimized,
            // copy over working directory like asif being run from same console.
            WorkingDirectory = Directory.GetCurrentDirectory(),
        };

        // Start the execution.
        lock (MStateLock)
        {
            Debug.Assert(mState == State.WaitingToExecute);
            mState = State.Executing;
        }

        var p = Process.Start(startinfo);
        p?.WaitForExit();

        Console.WriteLine("...cmd exited");

        // Wait here for windows lag???
    }
}