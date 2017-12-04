using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordDice
{
    // TimeLimitedMemory などのテストをしやすいよう、このインターフェースを設けている
    public interface ITime
    {
        DateTimeOffset GetUtcNow();
        TimeSpan TimeLimit { get; }
        TimeSpan CacheTimeLimit { get; }
        TimeSpan WindowOfCheckingTimeLimit { get; }
    }

    public sealed class Time : ITime
    {
        static Time()
        {
            Default = new Time();
        }

        private Time()
        {

        }

        public static Time Default { get; }

        public DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
        public TimeSpan TimeLimit { get => TimeSpan.FromMinutes(15); }
        public TimeSpan CacheTimeLimit { get => TimeSpan.FromMinutes(15); }
        public TimeSpan WindowOfCheckingTimeLimit { get => TimeSpan.FromMinutes(1); }
    }
}
