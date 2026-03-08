using System;
using StS2AP.Utils;

public static class LogUtility
{
    public static void Info(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] [INFO] {message}";
        ConsoleLogger.WriteLine(ConsoleColor.White, line);
    }

    public static void Warn(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] [WARN] {message}";
        ConsoleLogger.WriteLine(ConsoleColor.Yellow, line);
    }

    public static void Error(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] [ERROR] {message}";
        ConsoleLogger.WriteLine(ConsoleColor.Red, line);
    }

    public static void Debug(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] [DEBUG] {message}";
        ConsoleLogger.WriteLine(ConsoleColor.Gray, line);
    }

    public static void Success(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] [OK] {message}";
        ConsoleLogger.WriteLine(ConsoleColor.Green, line);
    }
}