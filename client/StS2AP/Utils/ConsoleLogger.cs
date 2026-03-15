using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace StS2AP.Utils;

/// <summary>
/// A TextWriter that writes to both the console and an internal buffer for crash logging.
/// </summary>
internal class BufferedConsoleWriter : TextWriter
{
    private readonly StreamWriter _consoleWriter;
    private readonly List<string> _buffer;
    private readonly object _bufferLock;
    private readonly StringBuilder _currentLine = new();

    public override Encoding Encoding => Encoding.UTF8;

    public BufferedConsoleWriter(StreamWriter consoleWriter, List<string> buffer, object bufferLock)
    {
        _consoleWriter = consoleWriter;
        _buffer = buffer;
        _bufferLock = bufferLock;
    }

    public override void Write(char value)
    {
        _consoleWriter.Write(value);

        lock (_bufferLock)
        {
            if (value == '\n')
            {
                _buffer.Add(_currentLine.ToString());
                _currentLine.Clear();
            }
            else if (value != '\r')
            {
                _currentLine.Append(value);
            }
        }
    }

    public override void Write(string? value)
    {
        if (value == null) return;

        _consoleWriter.Write(value);

        lock (_bufferLock)
        {
            foreach (char c in value)
            {
                if (c == '\n')
                {
                    _buffer.Add(_currentLine.ToString());
                    _currentLine.Clear();
                }
                else if (c != '\r')
                {
                    _currentLine.Append(c);
                }
            }
        }
    }

    public override void WriteLine(string? value)
    {
        _consoleWriter.WriteLine(value);

        lock (_bufferLock)
        {
            _currentLine.Append(value);
            _buffer.Add(_currentLine.ToString());
            _currentLine.Clear();
        }
    }

    public override void Flush()
    {
        _consoleWriter.Flush();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Flush any remaining partial line
            lock (_bufferLock)
            {
                if (_currentLine.Length > 0)
                {
                    _buffer.Add(_currentLine.ToString());
                    _currentLine.Clear();
                }
            }
            _consoleWriter.Dispose();
        }
        base.Dispose(disposing);
    }
}

public static class ConsoleLogger
{
    private static bool _initialized;
    private static BufferedConsoleWriter? _consoleWriter;
    private static readonly object _lock = new();
    private static readonly List<string> _logBuffer = new();

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

            // Redirect Console.Out to a buffered writer that captures all output
            var stdOut = Console.OpenStandardOutput();
            var streamWriter = new StreamWriter(stdOut) { AutoFlush = true };
            _consoleWriter = new BufferedConsoleWriter(streamWriter, _logBuffer, _lock);
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

    /// <summary>
    /// Dumps all buffered console output to a file.
    /// This includes all stdout output, not just LogUtility messages.
    /// Used for crash logging to preserve debug information.
    /// </summary>
    /// <param name="filePath">The full path to the output file.</param>
    public static void DumpToFile(string filePath)
    {
        lock (_lock)
        {
            try
            {
                File.WriteAllLines(filePath, _logBuffer);
            }
            catch
            {
                // Silently fail - we're likely crashing anyway
            }
        }
    }
}
