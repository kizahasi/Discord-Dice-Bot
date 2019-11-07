using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordDice
{
    public static class Loggers
    {
        static readonly LoggerFactory consoleLogger
       = new LoggerFactory(new[] { new ConsoleLoggerProvider((_, __) => true, true) });

        static readonly LoggerFactory emptyLogger = new LoggerFactory(Array.Empty<ILoggerProvider>());

        public static LoggerFactory ConsoleLogger { get; } = consoleLogger;

        public static LoggerFactory EmptyLogger { get; } = emptyLogger;
    }
}
