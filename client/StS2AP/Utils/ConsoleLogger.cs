using System;
using System.IO;
using System.Runtime.InteropServices;

namespace StS2AP.Utils;

public static class ConsoleLogger
{
    private static bool _initialized;
    private static StreamWriter? _consoleWriter;
    private static readonly object _lock = new();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleTitleW([MarshalAs(UnmanagedType.LPWStr)] string lpConsoleTitle);

    public static void Initialize()
    {
        lock (_lock)
        {
            if (_initialized)
                return;

            // Allocate a new console window
            AllocConsole();
            SetConsoleTitleW("Slay the Spire 2 - Archipelago Debug Console");

            // Redirect Console.Out to the new console
            var stdOut = Console.OpenStandardOutput();
            _consoleWriter = new StreamWriter(stdOut) { AutoFlush = true };
            Console.SetOut(_consoleWriter);

            _initialized = true;

            WriteLine(ConsoleColor.Cyan, "========================================");
            WriteLine(ConsoleColor.Cyan, "  STS2 Archipelago Debug Console");
            WriteLine(ConsoleColor.Cyan, "========================================");
            WriteLine(ConsoleColor.Gray, $"Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            WriteLine(ConsoleColor.Gray, "");
        }
    }

    public static void Shutdown()
    {
        lock (_lock)
        {
            if (!_initialized)
                return;

            WriteLine(ConsoleColor.Gray, "");
            WriteLine(ConsoleColor.Cyan, "Console shutting down...");

            _consoleWriter?.Dispose();
            _consoleWriter = null;

            FreeConsole();
            _initialized = false;
        }
    }

    public static void WriteLine(ConsoleColor color, string message)
    {
        lock (_lock)
        {
            if (!_initialized)
                return;

            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine(message);
                Console.ResetColor();
            }
            catch
            {
                // Ignore console write failures
            }
        }
    }

    public static void WriteLine(string message)
    {
        WriteLine(ConsoleColor.White, message);
    }
}
