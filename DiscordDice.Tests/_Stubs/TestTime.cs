using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordDice.Tests
{
    public class TestTime : ITime
    {
        public TestTime()
        {
            TimeLimit = TimeSpan.FromHours(1);
            CacheTimeLimit = TimeSpan.FromHours(1);
        }

        public TimeSpan TimeLimit { get; set; }
        public TimeSpan CacheTimeLimit { get; set; }
        public TimeSpan WindowOfCheckingTimeLimit { get => TimeSpan.FromSeconds(0.1); }
        public DateTimeOffset UtcNow { get; set; }
        public void AdvanceBy(TimeSpan time)
        {
            UtcNow = UtcNow + time;
        }

        DateTimeOffset ITime.GetUtcNow() => UtcNow;
    }
}
