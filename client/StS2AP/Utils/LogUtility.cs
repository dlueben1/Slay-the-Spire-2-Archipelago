using MegaCrit.Sts2.Core.Logging;

public static class LogUtility
{
    private static readonly Logger _logger = new Logger("AP", LogType.Generic);

    public static void Info(string message)
    {
        WriteColored(message, ConsoleColor.Cyan);
        _logger.Info(message);
    }

    public static void Warn(string message)
    {
        WriteColored(message, ConsoleColor.Yellow);
        _logger.Warn(message);
    }

    public static void Error(string message)
    {
        WriteColored(message, ConsoleColor.Red);
        _logger.Error(message);
    }

    public static void Debug(string message)
    {
        // Keep the console as quiet as the game log; launch with "-log Generic Debug" to see these.
        if (!_logger.WillLog(LogLevel.Debug))
            return;
        WriteColored(message, ConsoleColor.Gray);
        _logger.Debug(message);
    }

    public static void Success(string message)
    {
        WriteColored(message, ConsoleColor.Green);
        _logger.Info(message);
    }

    private static void WriteColored(string message, ConsoleColor color)
    {
        var previousColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine($"[AP] [{DateTime.Now:HH:mm:ss}] {message}");
        Console.ForegroundColor = previousColor;
    }
}