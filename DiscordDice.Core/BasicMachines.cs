using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

// 低レイヤーな処理を行うクラスの集合。
// 少し複雑(例えば内部状態を持つなど)な処理に向いている。単純すぎる処理はここに書くメリットは薄い。
// 様々な Command に参照されて使われる。
// メッセージ送信の処理は(合成が簡単なので) IObservable<_> を用いて通知することで表現している。
namespace DiscordDice.BasicMachines
{
    internal sealed class ScanMachine
    {
        readonly ILazySocketClient _client;
        // _core から削除されたものは、_finishedCoreCache に追加(or上書き)される。
        // _finishedCoreCache が存在する理由は、scanの終了後にシャッフルして再表示させる、あるいは単に再表示させるため。
        // TimeLimitedMemory の仕様を変更するのも面倒なので TimeLimitedMemory を 2 個用いている。
        readonly TimeLimitedMemory<(ulong channelId, ulong scanStartedUserId), Value> _core;
        readonly TimeLimitedMemory<(ulong channelId, ulong scanStartedUserId), Value> _finishedCoreCache;

        readonly Subject<Response> _sentResponse = new Subject<Response>();

        public ScanMachine(ILazySocketClient client, ITime time)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            if (time == null) throw new ArgumentNullException(nameof(time));

            _core = new TimeLimitedMemory<(ulong channelId, ulong scanStartedUserId), Value>(time.TimeLimit, time.WindowOfCheckingTimeLimit, time, new ConcurrentDictionaryMemory<(ulong, ulong), (Value, DateTimeOffset)>());
            _finishedCoreCache = new TimeLimitedMemory<(ulong channelId, ulong scanStartedUserId), Value>(time.CacheTimeLimit, time.WindowOfCheckingTimeLimit, time, new ConcurrentDictionaryMemory<(ulong, ulong), (Value, DateTimeOffset)>());
            _core.Updated
                .Subscribe(async e =>
                {
                    if (e.Type != TimeLimitedMemoryChangedType.Replaced)
                    {
                        _finishedCoreCache.AddOrUpdate(e.Key, e.OldValue);
                    }
                    if (e.Type == TimeLimitedMemoryChangedType.TimeLimit)
                    {
                        await RespondAsync(e.OldValue, e.Key.channelId, e.Key.scanStartedUserId, RespondType.ByTimeLimit);
                    }
                });
            SentResponse = _sentResponse.Where(r => r != null);
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

        private bool TryRemoveAndCache((ulong channelId, ulong scanStartedUserId) key, out Value value)
        {
            if (_core.TryRemove((key.channelId, key.scanStartedUserId), out var v))
            {
                _finishedCoreCache.AddOrUpdate(key, v);
                value = v;
                return true;
            }

            value = default;
            return false;
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
                .GetRolledDices()
                .Shuffle(type == RespondType.ShuffledShow)
                .Aggregate(new StringBuilder(firstLine), (resultBuilder, tuple) =>
                {
                    resultBuilder.Append("\r\n");
                    var (rank, user, rolledDice) = tuple;
                    var appending = $"{String.Format("{0:D2}", rank + 1)}位 - {user.Username}({rolledDice})";
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

        public async Task StartAsync(ulong channelId, ulong userId, bool force, Expr.Main expr, int maxSize, bool noProgress)
        {
            var key = (channelId, userId);
            var value = new Value(expr, maxSize, noProgress);
            if (force)
            {
                await EndAsync(channelId, userId, true);
            }
            if (_core.TryAdd(key, value))
            {
                await RespondBySayAsync($"{expr.ToString()} の集計が開始されました。", channelId, userId);
            }
            else
            {
                await RespondByCautionAsync("すでに別の集計が行われています。", channelId, userId);
            }
        }

        public async Task StartAsync(ILazySocketMessageChannel channel, ILazySocketUser user, bool force, Expr.Main expr, int maxSize, bool noProgress)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (user == null) throw new ArgumentNullException(nameof(user));
            await StartAsync(await channel.GetIdAsync(), await user.GetIdAsync(), force, expr, maxSize, noProgress);
        }

        public async Task GetCurrentOrCachedProgressAsync(ulong channelId, ulong userId, bool shuffled)
        {
            var key = (channelId, userId);
            if (_core.TryGetValue(key, out var value))
            {
                await RespondAsync(value.value, channelId, userId, shuffled ? RespondType.ShuffledShow : RespondType.Show);
            }
            else
            {
                if (_finishedCoreCache.TryGetValue(key, out var cache))
                {
                    await RespondAsync(cache.value, channelId, userId, shuffled ? RespondType.ShuffledShow : RespondType.Show);
                }
                else
                {
                    await RespondByCautionAsync("集計が見つかりません。", channelId, userId);
                }
            }
        }

        public async Task GetCurrentOrCachedProgressAsync(ILazySocketMessageChannel channel, ILazySocketUser user, bool shuffled)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (user == null) throw new ArgumentNullException(nameof(user));

            await GetCurrentOrCachedProgressAsync(await channel.GetIdAsync(), await user.GetIdAsync(), shuffled);
        }

        public async Task GetCachedOrCurrentProgressAsync(ulong channelId, ulong userId, bool shuffled)
        {
            var key = (channelId, userId);
            if (_finishedCoreCache.TryGetValue(key, out var cache))
            {
                await RespondAsync(cache.value, channelId, userId, shuffled ? RespondType.ShuffledShow : RespondType.Show);
            }
            else
            {
                if (_core.TryGetValue(key, out var value))
                {
                    await RespondAsync(value.value, channelId, userId, shuffled ? RespondType.ShuffledShow : RespondType.Show);
                }
                else
                {
                    await RespondByCautionAsync("集計が見つかりません。", channelId, userId);
                }
            }
        }

        public async Task AbortAsync(ulong channelId, ulong userId)
        {
            var key = (channelId, userId);
            if (TryRemoveAndCache(key, out var value))
            {
                await RespondAsync(value, channelId, userId, RespondType.Aborted);
            }
            else
            {
                await RespondByCautionAsync("集計が見つかりません。", channelId, userId);
            }
        }

        public async Task EndAsync(ulong channelId, ulong userId, bool suppressNotFoundResponse = false)
        {
            var key = (channelId, userId);
            if (TryRemoveAndCache(key, out var value))
            {
                await RespondAsync(value, channelId, userId, RespondType.Ended);
            }
            else
            {
                if (!suppressNotFoundResponse)
                {
                    await RespondByCautionAsync("集計は行われていません。", channelId, userId);
                }
            }
        }

        public async Task SetDiceAsync(ulong channelId, ulong rollingUserId, Expr.Main.Executed executedExpr)
        {
            var rollingPairs =
                _core
                .ToEnumerable()
                .Where(pair =>
                {
                    var (scanningChannelId, _) = pair.Key;
                    return scanningChannelId == channelId;
                });
            foreach (var pair in rollingPairs)
            {
                var value = pair.Value.Item1;
                if (!Expr.Main.AreEquivalent(executedExpr.Expr, value.WatchingExpr))
                {
                    continue;
                }
                await value.TrySetAsync(_client, rollingUserId, executedExpr);
                if (value.RolledDicesCount >= value.MaxSize)
                {
                    await EndAsync(channelId, pair.Key.scanStartedUserId);
                    return;
                }
                if (!value.NoProgress)
                {
                    await RespondAsync(value, channelId, pair.Key.scanStartedUserId, RespondType.TochuuKeika);
                }
            }
        }

        public sealed class User : IEquatable<User>
        {
            private User()
            {

            }

            public static async Task<User> CreateAsync(ILazySocketUser user)
            {
                if (user == null)
                {
                    throw new ArgumentNullException(nameof(user));
                }

                var result = new User();
                result.UserId = await user.GetIdAsync();
                result.Username = await user.GetUsernameAsync();
                return result;
            }

            public static async Task<User> TryCreateAsync(ILazySocketClient client, ulong userId)
            {
                if (client == null)
                {
                    throw new ArgumentNullException(nameof(client));
                }

                var user = await client.TryGetUserAsync(userId);
                if (user == null)
                {
                    return null;
                }
                var result = new User();
                result.UserId = userId;
                result.Username = await user.GetUsernameAsync();
                return result;
            }

            public ulong UserId { get; private set; }
            public string Username { get; private set; }

            public bool Equals(User other)
            {
                if (other == null)
                {
                    return false;
                }
                return UserId == other.UserId;
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as User);
            }

            public override int GetHashCode()
            {
                return UserId.GetHashCode();
            }

            public override string ToString()
            {
                return $"({UserId}, {Username})";
            }

            public static bool operator ==(User x, User y)
            {
                if ((object)x == null)
                {
                    return (object)y == null;
                }
                return x.Equals(y);
            }

            public static bool operator !=(User x, User y)
            {
                return !(x == y);
            }
        }

        public sealed class Value
        {
            // (ダイスの値, そのダイスを振って出したユーザー)
            // User はタイブレークによって降順にソートされる
            private readonly Dictionary<int, List<User>> _rolledDices = new Dictionary<int, List<User>>();
            private readonly object _gate = new object();

            public Value(Expr.Main expr, int maxSize, bool noProgress)
            {
                WatchingExpr = expr ?? throw new ArgumentNullException(nameof(expr));
                MaxSize = maxSize;
                NoProgress = noProgress;
            }

            public Expr.Main WatchingExpr { get; }
            public int MaxSize { get; }
            public bool NoProgress { get; }

            private bool ContainsUser(User user)
            {
                return
                    _rolledDices
                    .SelectMany(pair => pair.Value)
                    .Any(u => u == user);
            }

            private static void InsertRandom<T>(IList<T> source, T item)
            {
                var index = Random.Next(0, source.Count + 1);
                source.Insert(index, item);
            }

            public async Task<bool> TrySetAsync(ILazySocketClient client, ulong userId, Expr.Main.Executed executedExpr)
            {
                if (client == null)
                {
                    throw new ArgumentNullException(nameof(client));
                }

                var userValue = await User.TryCreateAsync(client, userId);
                if (userValue == null || ContainsUser(userValue))
                {
                    return false;
                }
                if (_rolledDices.TryGetValue(executedExpr.Value, out var users))
                {
                    InsertRandom(users, userValue);
                    return true;
                }

                _rolledDices[executedExpr.Value] = new List<User> { userValue };
                return true;
            }

            public int RolledDicesCount
            {
                get
                {
                    return
                        _rolledDices
                        .SelectMany(pair => pair.Value.Select(value => new { Key = pair.Key, Value = value }))
                        .Count();
                }
            }

            public IReadOnlyList<(int rank, User user, int rolledDice)> GetRolledDices()
            {
                return
                    _rolledDices
                    .OrderByDescending(pair => pair.Key)
                    .SelectMany(pair => pair.Value.Select(value => new { Key = pair.Key, Value = value }))
                    .Select((pair, i) => (i, pair.Value, pair.Key))
                    .ToArray()
                    .ToReadOnly();
            }
        }
    }

    internal sealed class AllInstances
    {
        public AllInstances(ILazySocketClient client, ITime time)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }
            Scan = new ScanMachine(client, time ?? throw new ArgumentNullException(nameof(time)));
            SentResponse = Scan.SentResponse;
        }

        public IObservable<Response> SentResponse { get; }
        public ScanMachine Scan { get; }
    }
}
