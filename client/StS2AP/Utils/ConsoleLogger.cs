using System;
using System.IO;
using System.Runtime.InteropServices;

namespace StS2AP.Utils;

public static class ConsoleLogger
{
    private static bool _initialized;
    private static StreamWriter? _consoleWriter;
    private static readonly object _lock = new();

    #region Win32 API
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleTitleW([MarshalAs(UnmanagedType.LPWStr)] string lpConsoleTitle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    // constants to disable quick edit mode, and fixing the issue with the terminal
    private const int STD_INPUT_HANDLE = -10;
    private const uint ENABLE_EXTENDED_FLAGS = 0x0080;
    private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
    #endregion

    public static void Initialize()
    {
        lock (_lock)
        {
            if (_initialized)
                return;

            // Allocate a new console window
            AllocConsole();
            SetConsoleTitleW("Slay the Spire 2 - Archipelago Debug Console");

            // disabling the quick edit mode
            DisableQuickEdit(false);

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

    private static void DisableQuickEdit(bool enable)
    {
        IntPtr consoleHandle = GetStdHandle(STD_INPUT_HANDLE);
        if (GetConsoleMode(consoleHandle, out uint consoleMode))
        {
            if (enable)
                consoleMode |= ENABLE_QUICK_EDIT_MODE;
            else
                consoleMode &= ~ENABLE_QUICK_EDIT_MODE;
            
            consoleMode |= ENABLE_EXTENDED_FLAGS;
            SetConsoleMode(consoleHandle, consoleMode);
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
