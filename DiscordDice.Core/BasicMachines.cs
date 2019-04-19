using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Reactive;

// 低レイヤーな処理を行うクラスの集合。
// 少し複雑(例えば内部状態を持つなど)な処理に向いている。単純すぎる処理はここに書くメリットは薄い。
// 様々な Command に参照されて使われる。
// メッセージ送信の処理は(合成が簡単なので) IObservable<_> を用いて通知することで表現している。
namespace DiscordDice.BasicMachines
{
    internal sealed class ScanMachine
    {
        readonly ILazySocketClient _client;
        readonly Subject<Response> _sentResponse = new Subject<Response>();
        readonly IConfig _config;
        DateTimeOffset _lastUpdatedScans = DateTimeOffset.MinValue;

        public ScanMachine(ILazySocketClient client, IConfig config)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            SentResponse = _sentResponse.Where(r => r != null);
        }

        /* 
         * データベースのScansをアップデートできる状態かどうかを見て、できる状態ならアップデートする。
         * アップデートできる状態かどうかを見る処理は重くないため、（よほど大量でなければ）多めに実行しても構わない。
         * 
         * - Observable.IntervalなどからこのTryUpdateScansAsync()を実行すれば楽に書けそうだが、Discord.NETのイベントから実行している理由
         * Observable.Intervalは（工夫しなければ）別のスレッドから実行されるので、とあるスレッドがMainDbContextを操作している際に別のスレッドがMainDbContextを操作しようとすることが起こり得る。これが起こると、IOExceptionが投げられてしまう。
         * これを回避するにはlockを使うという手もあるが、処理が重くなるという欠点がある。
         * そこで、TryUpdateScansAsync()（およびこのScanMachineのpublicメソッド全て）を全てDiscordのイベントから呼び出すことで、衝突を回避している。この方法の欠点はDiscord.NETのイベントの発火の頻度が少ないほどチェックのタイミングが不正確になることだが、別に正確性を求める処理ではないのであまり問題はない。
         */
        public async Task TryUpdateScansAsync()
        {
            var utcNow = _config.GetUtcNow();
            if (utcNow - _lastUpdatedScans >= _config.IntervalOfUpdatingScans)
            {
                _lastUpdatedScans = utcNow;
                await UpdateScansAsync();
            }
        }

        private async Task UpdateScansAsync()
        {
            var utcNow = _config.GetUtcNow();
            using (var context = new MainDbContext(_config))
            {
                var archiving = await
                    context.Scans
                    .Where(s => !s.IsArchived)
                    .Where(s => utcNow - s.StartedAt >= _config.TimeToMakeScanArchived)
                    .Include(s => s.ScanRolls)
                    .ThenInclude(s => s.User)
                    .ToArrayAsync();
                foreach (var a in archiving)
                {
                    a.IsArchived = true;
                    if (ulong.TryParse(a.ChannelID, out var channelID) && ulong.TryParse(a.ScanStartedUserID, out var scanStartedUserID))
                    {
                        await RespondAsync(new Value(a), channelID, scanStartedUserID, RespondType.ByTimeLimit);
                    }
                }

                var removing =
                    await
                    context.Scans
                    .Where(s => s.IsArchived)
                    .Where(s => utcNow - s.StartedAt >= _config.TimeToMakeScanRemoved)
                    .ToArrayAsync();
                foreach (var r in removing)
                {
                    context.Scans.Remove(r);
                }
                await context.SaveChangesAsync();
            }
        }

        public IObservable<Response> SentResponse { get; }

        private enum RespondType
        {
            Ended,
            Aborted,
            Show, // scan-show
            ShuffledShow, // scan-show --shuffled
            TochuuKeika,
            ByTimeLimit,
        }

        private async Task<IReadOnlyList<Value>> TryArchiveAsync(ulong channelId, ulong scanStartedUserId)
        {
            IReadOnlyList<Value> result;
            using (var context = new MainDbContext(_config))
            {
                var archiving =
                    await
                    context.Scans
                    .Where(s => !s.IsArchived)
                    .Where(s => s.ChannelID == channelId.ToString())
                    .Where(s => s.ScanStartedUserID == scanStartedUserId.ToString())
                    .Include(s => s.ScanRolls)
                    .ThenInclude(s => s.User)
                    .ToArrayAsync();
                foreach (var a in archiving)
                {
                    a.IsArchived = true;
                }
                result = archiving.Select(a => new Value(a)).ToArray().ToReadOnly();
                await context.SaveChangesAsync();
            }
            return result;
        }

        private async Task RespondAsync(Value value, ulong channelId, ulong scanStartedUserId, RespondType type)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (type == RespondType.Aborted)
            {
                await RespondBySayAsync($"{value.WatchingExpr.ToString()} の集計は停止されました。", channelId, scanStartedUserId);
                return;
            }

            string infoText;
            switch (type)
            {
                case RespondType.TochuuKeika:
                case RespondType.Show:
                    infoText = "(途中経過)";
                    break;
                case RespondType.ShuffledShow:
                    infoText = "(途中経過: 一時的なシャッフル)";
                    break;
                default:
                    infoText = "";
                    break;
            }
            var firstLine = $"```{value.WatchingExpr.ToString()} の集計結果{infoText}:";
            var text =
                value
                .Ranking
                .Shuffle(type == RespondType.ShuffledShow)
                .Aggregate(new StringBuilder(firstLine), (resultBuilder, tuple) =>
                {
                    resultBuilder.Append("\r\n");
                    var (rank, userID, userName, rolledDice) = tuple;
                    var appending = $"{String.Format("{0:D2}", rank + 1)}位 - {userName}({rolledDice})";
                    resultBuilder.Append(appending);
                    return resultBuilder;
                })
                .Append("```")
                .ToString();
            await RespondBySayAsync(text, channelId, scanStartedUserId);
        }

        private async Task RespondBySayAsync(string text, ulong channelId, ulong? userIdOfReplyTo = null)
        {
            var response = await Response.TryCreateSayAsync(_client, text, channelId, userIdOfReplyTo);
            if (response == null)
            {
                return;
            }
            _sentResponse.OnNext(response);
        }

        private async Task RespondByCautionAsync(string text, ulong channelId, ulong? userIdOfReplyTo = null)
        {
            var response = await Response.TryCreateCautionAsync(_client, text, channelId, userIdOfReplyTo);
            if (response == null)
            {
                return;
            }
            _sentResponse.OnNext(response);
        }

        public async Task StartAsync(ulong channelId, ulong userId, string username, bool force, Expr.Main expr, int maxSize, bool noProgress)
        {
            if (force)
            {
                await EndAsync(channelId, userId, true);
            }
            using (var context = new MainDbContext(_config))
            {
                var scan =
                    await
                    context.Scans
                    .Where(s => !s.IsArchived)
                    .FirstOrDefaultAsync(s => s.ChannelID == channelId.ToString() && s.ScanStartedUserID == userId.ToString());
                if (scan == null)
                {
                    await AddOrUpdateUserAsync(context, userId, username);
                    var newScan =
                        new Models.Scan
                        {
                            ChannelID = channelId.ToString(),
                            ScanStartedUserID = userId.ToString(),
                            StartedAt = _config.GetUtcNow(),
                            Expr = expr,
                            MaxSize = maxSize,
                            NoProgress = noProgress,
                        };
                    await context.Scans.AddAsync(newScan);
                }
                else
                {
                    await RespondByCautionAsync("すでに別の集計が行われています。", channelId, userId);
                    return;
                }
                await context.SaveChangesAsync();
            }

            await RespondBySayAsync($"{expr.ToString()} の集計が開始されました。", channelId, userId);
        }

        public async Task StartAsync(ILazySocketMessageChannel channel, ILazySocketUser user, bool force, Expr.Main expr, int maxSize, bool noProgress)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (user == null) throw new ArgumentNullException(nameof(user));
            await StartAsync(await channel.GetIdAsync(), await user.GetIdAsync(), await user.GetUsernameAsync(), force, expr, maxSize, noProgress);
        }

        public async Task GetLatestProgressAsync(ulong channelId, ulong userId, bool shuffled)
        {
            Models.Scan scan;
            using (var context = new MainDbContext(_config))
            {
                scan =
                    await
                    context.Scans
                    .Where(s => s.ChannelID == channelId.ToString() && s.ScanStartedUserID == userId.ToString())
                    .OrderBy(s => s.IsArchived)
                    .ThenByDescending(s => s.StartedAt)
                    .Include(s => s.ScanRolls)
                    .ThenInclude(s => s.User)
                    .FirstOrDefaultAsync();
            }
            if (scan == null)
            {
                await RespondByCautionAsync("集計が見つかりません。", channelId, userId);
                return;
            }
            var value = new Value(scan);
            await RespondAsync(value, channelId, userId, shuffled ? RespondType.ShuffledShow : RespondType.Show);
        }

        public async Task GetLatestProgressAsync(ILazySocketMessageChannel channel, ILazySocketUser user, bool shuffled)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (user == null) throw new ArgumentNullException(nameof(user));

            await GetLatestProgressAsync(await channel.GetIdAsync(), await user.GetIdAsync(), shuffled);
        }

        public async Task AbortAsync(ulong channelId, ulong userId)
        {
            var archived = await TryArchiveAsync(channelId, userId);
            if (archived.Count == 0)
            {
                await RespondByCautionAsync("集計が見つかりません。", channelId, userId);
                return;
            }
            foreach (var a in archived)
            {
                await RespondAsync(a, channelId, userId, RespondType.Aborted);
            }
        }

        public async Task EndAsync(ulong channelId, ulong userId, bool suppressNotFoundResponse = false)
        {
            var archived = await TryArchiveAsync(channelId, userId);
            if (archived.Count == 0)
            {
                if (!suppressNotFoundResponse)
                {
                    await RespondByCautionAsync("集計が見つかりません。", channelId, userId);
                }
                return;
            }
            foreach (var a in archived)
            {
                await RespondAsync(a, channelId, userId, RespondType.Ended);
            }
        }

        private async Task AddOrUpdateUserAsync(MainDbContext context, ulong userId, string username)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var found = await context.Users.FirstOrDefaultAsync();
            if (found == null)
            {
                await context.Users.AddAsync(new Models.User { ID = userId.ToString(), Username = username });
                return;
            }
            found.Username = username;
        }

        public async Task SetDiceAsync(ulong channelId, ulong rollingUserId, string rollingUserName, Expr.Main.Executed executedExpr)
        {
            var scanIDsToRespond = new List<string>();
            using (var context = new MainDbContext(_config))
            {
                var notArhivedScans =
                    await
                    context.Scans
                    .Where(s => s.ChannelID == channelId.ToString() && !s.IsArchived)
                    .Include(s => s.ScanRolls)
                    .ThenInclude(s => s.User)
                    .ToArrayAsync();
                foreach (var scan in notArhivedScans)
                {
                    await AddOrUpdateUserAsync(context, rollingUserId, rollingUserName);

                    if (!Expr.Main.AreEquivalent(scan.Expr, executedExpr.Expr))
                    {
                        continue;
                    }
                    if (!scan.ScanRolls.Any(s => s.UserID == rollingUserId.ToString()))
                    {
                        var scanRoll = new Models.ScanRoll
                        {
                            UserID = rollingUserId.ToString(),
                            ScanID = scan.ID,
                            Value = executedExpr.Value,
                            ValueTieBreaker = Guid.NewGuid().ToString()
                        };
                        await context.ScanRolls.AddAsync(scanRoll);
                    }
                    scanIDsToRespond.Add(scan.ID);
                }
                await context.SaveChangesAsync();

                foreach (var scanID in scanIDsToRespond)
                {
                    var scan =
                        await
                        context.Scans
                        .Where(s => s.ID == scanID)
                        .Include(s => s.ScanRolls)
                        .ThenInclude(s => s.User)
                        .FirstOrDefaultAsync();
                    if (scan == null)
                    {
                        continue;
                    }
                    if (!ulong.TryParse(scan.ScanStartedUserID, out var scanStartedUserID))
                    {
                        continue;
                    }
                    if (scan.ScanRolls.Count >= scan.MaxSize)
                    {
                        await EndAsync(channelId, scanStartedUserID);
                        continue;
                    }
                    if (!scan.NoProgress)
                    {
                        await RespondAsync(new Value(scan), channelId, scanStartedUserID, RespondType.TochuuKeika);
                    }
                }
            }
        }

        public sealed class Value
        {
            // Models.ScanのScanRollsとUserをIncludeしておくことを忘れないように！
            public Value(Models.Scan scan)
            {
                if (scan == null) throw new ArgumentNullException(nameof(scan));
                if (scan.ScanRolls == null) throw new ArgumentException(nameof(scan.ScanRolls));

                WatchingExpr = scan.Expr ?? Expr.Main.Invalid;
                MaxSize = scan.MaxSize;
                NoProgress = scan.NoProgress;
                IsArchived = scan.IsArchived;
                Ranking =
                    scan.ScanRolls
                    .Where(r => r != null)
                    .OrderByDescending(r => r.Value)
                    .ThenByDescending(r => r.ValueTieBreaker)
                    .SelectMany((r, i) =>
                    {
                        if (ulong.TryParse(r.UserID, out var userID))
                        {
                            return new[] { (i, userID, r.User.Username, r.Value) };
                        }
                        return new (int, ulong, string, int)[0];
                    })
                    .ToArray()
                    .ToReadOnly();
            }

            public Expr.Main WatchingExpr { get; }
            public int MaxSize { get; }
            public bool NoProgress { get; }
            public bool IsArchived { get; }

            public IReadOnlyList<(int rank, ulong userId, string userName, int rolledDice)> Ranking { get; }
        }
    }

    internal sealed class AllInstances
    {
        public AllInstances(ILazySocketClient client, IConfig config)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }
            Scan = new ScanMachine(client, config ?? throw new ArgumentNullException(nameof(config)));
            SentResponse = Scan.SentResponse;
        }

        public IObservable<Response> SentResponse { get; }
        public ScanMachine Scan { get; }
    }
}
