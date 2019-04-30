using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace DiscordDice.Tests
{
    public class TestConfig : IConfig
    {
        public TestConfig()
        {

        }

        public DateTimeOffset UtcNow { get; set; }

        public string DatabaseConnectionString
        {
            get
            {
                var dataSource = "main-test.sqlite";
                return new SqliteConnectionStringBuilder { DataSource = dataSource }.ToString();
            }
        }

        public TimeSpan IntervalOfUpdatingScans { get; set; } = TimeSpan.FromSeconds(1);

        public TimeSpan TimeToMakeScanArchived => TimeSpan.FromHours(1);

        public TimeSpan TimeToMakeScanRemoved => TimeSpan.FromHours(2);

        public void AdvanceBy(TimeSpan time)
        {
            UtcNow = UtcNow + time;
        }

        DateTimeOffset IConfig.GetUtcNow() => UtcNow;
    }
}
