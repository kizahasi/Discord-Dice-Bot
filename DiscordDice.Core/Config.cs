using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace DiscordDice
{
    // テストをしやすいように、このインターフェースを設けている
    public interface IConfig
    {
        DateTimeOffset GetUtcNow();

        // データベースのScanをチェックする時間。チェックする内容は、例えば作成から一定時間経過したもののIsArchivedをtrueにするなど。例えば3分なら3分ごとにデータベースをチェックする。
        TimeSpan IntervalOfUpdatingScans { get; }

        // 作成からこの時間以上経ったScanのIsArchivedを自動的にtrueにする。
        TimeSpan TimeToMakeScanArchived { get; }

        // 作成からこの時間以上経ったScanをデータベースから削除する。
        TimeSpan TimeToMakeScanRemoved { get; }

        string DatabaseConnectionString { get; }
    }

    public sealed class Config : IConfig
    {
        static Config()
        {
            Default = new Config();
        }

        private Config()
        {

        }

        public static Config Default { get; }

        public DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;

        public TimeSpan IntervalOfUpdatingScans => TimeSpan.FromMinutes(5);

        public TimeSpan TimeToMakeScanArchived => TimeSpan.FromMinutes(15);

        public TimeSpan TimeToMakeScanRemoved => TimeSpan.FromHours(2);

        public string DatabaseConnectionString
        {
            get
            {
                var dataSource = Environment.IsRelease ? "main.sqlite" : "main-debug.sqlite";
                return new SqliteConnectionStringBuilder { DataSource = dataSource }.ToString();
            }
        }
    }
}
