using Discord.Net;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace DiscordDice
{
    public enum ResponseType
    {
        None,
        Say,
        Caution,
    }

    public sealed class Response
    {
        private Response(ResponseType type, string message, ILazySocketMessageChannel channel, ILazySocketUser replyTo = null)
        {
            Type = type;
            Message = message;
            Channel = channel;
            ReplyTo = replyTo;
        }

        public ResponseType Type { get; }
        public string Message { get; }
        public ILazySocketMessageChannel Channel { get; }
        public ILazySocketUser ReplyTo { get; }
        public async Task<string> GetMessageWithMentionAsync()
        {
            var mention = ReplyTo == null ? null : await ReplyTo.GetMentionAsync();
            if (mention == null)
            {
                return Message ?? "";
            }
            else
            {
                return $"{mention} {Message ?? ""}";
            }
        }

        public static Response None { get; } = new Response(ResponseType.None, null, null, null);

        private static Response TryCreate(ResponseType type, string message, ILazySocketMessageChannel channel, ILazySocketUser replyTo = null)
        {
            if (message.Length >= 1500) // Discordの文字上限は2000文字(2019/05/02現在)
            {
                return new Response(ResponseType.Caution, "文字列が長すぎるため、結果を返せませんでした。", channel, replyTo);
            }
            return new Response(type, message, channel, replyTo);
        }

        private static async Task<Response> TryCreateAsync(ResponseType type, ILazySocketClient client, string message, ulong channelId, ulong? userIdOfReplyTo = null)
        {
            var channel = await client.TryGetMessageChannelAsync(channelId);
            if (userIdOfReplyTo == null)
            {
                return TryCreate(type, message, channel);
            }
            var replyTo = await client.TryGetUserAsync(userIdOfReplyTo.Value);
            if (replyTo == null)
            {
                return null;
            }
            return TryCreate(type, message, channel, replyTo);
        }

        public static Response TryCreateSay(string message, ILazySocketMessageChannel channel, ILazySocketUser replyTo = null)
        {
            return TryCreate(ResponseType.Say, message, channel, replyTo);
        }

        public static async Task<Response> TryCreateSayAsync(ILazySocketClient client, string message, ulong channelId, ulong? userIdOfReplyTo = null)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            return await TryCreateAsync(ResponseType.Say, client, message, channelId, userIdOfReplyTo);
        }

        public static Response TryCreateCaution(string message, ILazySocketMessageChannel channel, ILazySocketUser replyTo = null)
        {
            return TryCreate(ResponseType.Caution, message, channel, replyTo);
        }

        public static async Task<Response> TryCreateCautionAsync(ILazySocketClient client, string message, ulong channelId, ulong? userIdOfReplyTo = null)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            return await TryCreateAsync(ResponseType.Caution, client, message, channelId, userIdOfReplyTo);
        }
    }

    // API 制限をなるべく緩和しつつ投稿するクラス
    public sealed class ResponsesSender
    {
        readonly TimeSpan _bufferTime = TimeSpan.FromSeconds(3);
        // API制限に引っかかったときに次に投稿を試みるまでの時間を示すが、現在のコードだと最大で _bufferTime の誤差が生じることがある
        readonly TimeSpan _retryTime = TimeSpan.FromSeconds(30);

        readonly object _gate = new object();
        // (channelId, _)
        readonly Dictionary<ulong, CacheValue> _cache = new Dictionary<ulong, CacheValue>();
        readonly IDisposable _subscription;

        private ResponsesSender(IObservable<Response> responseSent)
        {
            if (responseSent == null) throw new ArgumentNullException(nameof(responseSent));

            _subscription =
                responseSent
                .Buffer(_bufferTime)
                .Synchronize(_gate)
                .SubscribeAsync(async responses =>
                {
                    foreach (var r in responses)
                    {
                        await AddCacheAsync(r);
                    }

                    foreach (var pair in _cache.ToArray())
                    {
                        if (pair.Value.LatestRateLimit.HasValue
                            && DateTimeOffset.UtcNow - pair.Value.LatestRateLimit.Value < _retryTime)
                        {
                            return;
                        }

                        var isSuccess = await TrySendAsync(pair);
                        if (isSuccess)
                        {
                            _cache.Remove(pair.Key);
                        }
                        else
                        {
                            pair.Value.LatestRateLimit = DateTimeOffset.UtcNow;
                        }
                    }
                });
        }

        public static IDisposable Start(IObservable<Response> responseSent)
        {
            if (responseSent == null) throw new ArgumentNullException(nameof(responseSent));

            return new ResponsesSender(responseSent)._subscription;
        }

        private async Task AddCacheAsync(Response response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));

            if(response.Type == ResponseType.None)
            {
                return;
            }

            var chennelId = await response.Channel.GetIdAsync();
            if (_cache.TryGetValue(chennelId, out var value))
            {
                value.Responses.Add(response);
            }
            else
            {
                var responses = new List<Response> { response };
                _cache[chennelId] = new CacheValue(response.Channel, responses);
            }
        }

        // キャッシュは ResponsesSender ではなく Response のほうで行ったほうが綺麗だと思う
        private static async Task<string> GetMessageWithMentionAsync(IEnumerable<Response> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var resultBuilder = new StringBuilder();
            var isFirst = true;

            foreach(var response in source.Where(response => response.Type != ResponseType.None))
            {
                if(!isFirst)
                {
                    resultBuilder.Append("\r\n");
                }

                resultBuilder.Append(await response.GetMessageWithMentionAsync());

                isFirst = false;
            }

            return resultBuilder.ToString();
        }

        // キャッシュから削除していいなら true を、そうでないなら false を返す
        private static async Task<bool> TrySendAsync(KeyValuePair<ulong, CacheValue> source)
        {
            var channelId = source.Key;
            var message = await GetMessageWithMentionAsync(source.Value.Responses);

            if(string.IsNullOrWhiteSpace(message))
            {
                return true;
            }

            try
            {
                await source.Value.Channel.SendMessageAsync(message);
                ConsoleEx.WriteSentMessage(await source.Value.Channel.GetNameAsync(), message);
                return true;
            }
            catch (RateLimitedException)
            {
                ConsoleEx.WriteCaution($"{nameof(RateLimitedException)} @ {channelId}: Sending messege is delayed.");
                return false;
            }
        }

        // Channel を除いて mutable
        class CacheValue
        {
            public CacheValue(ILazySocketMessageChannel channel, IList<Response> responses)
            {
                Channel = channel ?? throw new ArgumentNullException(nameof(channel));
                Responses = responses ?? throw new ArgumentNullException(nameof(responses));
            }

            public ILazySocketMessageChannel Channel { get; }
            public IList<Response> Responses { get; }
            public DateTimeOffset? LatestRateLimit { get; set; }

        }
    }
}
