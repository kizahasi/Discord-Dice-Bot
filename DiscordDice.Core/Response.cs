﻿using Discord.Net;
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
        private readonly Subject<RateLimitedResponse> rateLimitedResponses = new Subject<RateLimitedResponse>();

        private Response(ResponseType type, string message, ILazySocketMessageChannel channel, ILazySocketUser replyTo = null)
        {
            Type = type;
            Message = message;
            Channel = channel;
            ReplyTo = replyTo;

            SubscribeRateLimitedResponses(rateLimitedResponses);
        }

        private static void SubscribeRateLimitedResponses(IObservable<RateLimitedResponse> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            source
                .Delay(TimeSpan.FromMinutes(Random.Next(2, 6)))
                .Subscribe();
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

        static Response none = new Response(ResponseType.None, null, null, null);
        public static Response None
        {
            get
            {
                return none;
            }
        }

        public static Response CreateSay(string message, ILazySocketMessageChannel channel, ILazySocketUser replyTo = null)
        {
            return new Response(ResponseType.Say, message, channel ?? throw new ArgumentNullException(nameof(channel)), replyTo);
        }

        public static Response CreateCaution(string message, ILazySocketMessageChannel channel, ILazySocketUser replyTo = null)
        {
            return new Response(ResponseType.Caution, message, channel ?? throw new ArgumentNullException(nameof(channel)), replyTo);
        }

        private class RateLimitedResponse
        {
            public RateLimitedResponse(Response response, DateTimeOffset rateLimitedOn)
            {
                Response = response ?? throw new ArgumentNullException(nameof(response));
                RateLimitedOn = rateLimitedOn;
            }

            public Response Response { get; set; }
            public DateTimeOffset RateLimitedOn { get; set; }
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
                .Subscribe(async responses =>
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