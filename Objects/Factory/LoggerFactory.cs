/*using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MetroLog;
using MetroLog.Targets;
using MetroLog.MicrosoftExtensions;
using MetroLog.Internal;
using MetroLog.Layouts;
using System.Text.RegularExpressions;
using System.IO;
using Microsoft.Extensions.Caching;
namespace NetworkMonitor.Objects.Factory
{
    public class ConsoleColourTarget : SyncTarget
    {
        public ConsoleColourTarget()
            : this(new SingleLineLayout())
        {
        }
        public ConsoleColourTarget(Layout layout)
            : base(layout)
        {
        }
        protected override void Write(LogWriteContext context, LogEventInfo entry)
        {
            var message = Layout.GetFormattedString(context, entry);
            var color = GetColor(entry);
            WriteColor(message, color);
        }
        private ConsoleColor GetColor(LogEventInfo info)
        {
            ConsoleColor color = ConsoleColor.White;
            switch (info.Level)
            {
                case LogLevel.Trace:
                    color = ConsoleColor.DarkGray;
                    break;
                case LogLevel.Debug:
                    color = ConsoleColor.Gray;
                    break;
                case LogLevel.Info:
                    color = ConsoleColor.Green;
                    break;
                case LogLevel.Warn:
                    color = ConsoleColor.Yellow;
                    break;
                case LogLevel.Error:
                    color = ConsoleColor.Red;
                    break;
                case LogLevel.Fatal:
                    color = ConsoleColor.DarkRed;
                    break;
            }
            return color;
        }
        void WriteColor(string message, ConsoleColor color)
        {
            if (IsConsoleColorSupported())
            {
                var pieces = Regex.Split(message, @"(\[[^\]]*\])");
                for (int i = 0; i < pieces.Length; i++)
                {
                    string piece = pieces[i];
                    if (piece.StartsWith("[") && piece.EndsWith("]"))
                    {
                        Console.ForegroundColor = color;
                        piece = piece.Substring(1, piece.Length - 2);
                    }
                    Console.Write(piece);
                    Console.ResetColor();
                }
                Console.WriteLine();
            }
            else
            {
                // For platforms that don't support console color, just write plain message
                Console.WriteLine(" WARNING Console Log Colour not supported : " + message);
            }
        }

        bool IsConsoleColorSupported()
        {
            try
            {
                Console.ForegroundColor = Console.ForegroundColor;
                Console.ResetColor();
                return true;
            }
            catch (PlatformNotSupportedException)
            {
                return false;
            }
        }

    }
    public class SimpleLayout : Layout
    {
        public override string GetFormattedString(LogWriteContext context, LogEventInfo info)
        {
            return $"{info.TimeStamp:G} - [{info.Level} - {info.Message}]";
        }
    }
    public interface INetLoggerFactory
    {
        ILogger GetLogger(string loggerName);
    }
    public class NetLoggerFactory : INetLoggerFactory
    {
        private LoggingConfiguration _configLog = new LoggingConfiguration();

        public NetLoggerFactory()
        {
            LogLevel lowestLogLevel = LogLevel.Info;
            SetupLogger(lowestLogLevel);
        }
        public NetLoggerFactory(LogLevel lowestLogLevel)
        {
            SetupLogger(lowestLogLevel);
        }

        private void SetupLogger(LogLevel lowestLogLevel)
        {

            // will write logs to the console output (Logcat for android)
            _configLog.AddTarget(
                lowestLogLevel,
                LogLevel.Fatal,
                new ConsoleColourTarget(new SimpleLayout())
            );
        }
        public ILogger GetLogger(string loggerName)
        {
            LoggerFactory.Initialize(_configLog);
            return LoggerFactory.GetLogger(loggerName);
        }
      
    }
    public class NetTraceLoggerFactory : INetLoggerFactory
    {
        private LoggingConfiguration _configLog = new LoggingConfiguration();

        public NetTraceLoggerFactory()
        {
            LogLevel lowestLogLevel = LogLevel.Info;
            SetupLogger(lowestLogLevel);
        }
        public NetTraceLoggerFactory(LogLevel lowestLogLevel)
        {
            SetupLogger(lowestLogLevel);
        }

        private void SetupLogger(LogLevel lowestLogLevel)
        {

            _configLog.AddTarget(
                lowestLogLevel,
                LogLevel.Fatal,
                new TraceTarget(new SimpleLayout())
            );
        }
        public ILogger GetLogger(string loggerName)
        {
            LoggerFactory.Initialize(_configLog);
            return LoggerFactory.GetLogger(loggerName);
        }
    }
}
*/