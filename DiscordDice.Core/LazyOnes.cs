using Discord.Rest;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;

// SocketUser や ISocketMessageChannel などには、様々なプロパティや投稿メソッドがある。
// しかし、後者はともかく、前者はテスト向けのインスタンスを作ることが困難なので、そのままいろんな場所で使うとテストを書きづらくなってしまう。
// そこで、ILazy*** インターフェースを定義して、それを使っている。
//
// 当初は、DiscordSocketClient は使用した SocketUser や ISocketMessageChannel をキャッシュしており ID を渡せばそれを返ことができるっぽいことに着目して↓のようなクラスを書こうと思ったが、例えば LazySocketMessage から .Channel を取得するときに困ったので却下。
//public sealed class LazySocketUser
//{
//    readonly ulong _id;
//    readonly SocketUser _socketUser;
//    public LazySocketUser(ulong id)
//    {
//        _id = id;
//    }
//    public LazySocketUser(SocketUser socketUser)
//    {
//        _socketUser = socketUser ?? throw new ArgumentNullException(nameof(socketUser));
//    }
//    public ulong Id
//    {
//        get
//        {
//            if (_socketUser != null)
//            {
//                return _socketUser.Id;
//            }
//            return _id;
//        }
//    }
//    public SocketUser TryGetUser(DiscordSocketClient client = null)
//    {
//        if (_socketUser != null)
//        {
//            return _socketUser;
//        }
//        if (client == null)
//        {
//            return _socketUser;
//        }
//        return client.GetUser(_id);
//    }
//}
namespace DiscordDice
{
    // 各インターフェース のメソッドの戻り値の Task.Result() が null を返してもいいかそうでないかというルールの定義は、Discord.NET や Discord API の仕様を詳しく調べてないのでなんとも言えない。
    // NullReferenceException が発生したら適宜修正していく感じで。
    // 後々どうなるかわからないのでとりあえず全部 async にしているが、ゴリ押しすぎるかもしれない…
    public interface ILazySocketUser
    {
        Task<ulong> GetIdAsync();
        Task<string> GetMentionAsync();
        Task<string> GetUsernameAsync();
        Task<bool> GetIsBotAsync();
    }
    public interface ILazySocketMessageChannel
    {
        Task<ulong> GetIdAsync();
        Task<string> GetNameAsync();
        Task<RestUserMessage> SendMessageAsync(string text);
    }
    public interface ILazySocketMessage
    {
        Task<ILazySocketUser> GetAuthorAsync();
        Task<ILazySocketMessageChannel> GetChannelAsync();
        Task<string> GetContentAsync();
        Task<IReadOnlyCollection<ILazySocketUser>> GetMentionedUsersAsync();
    }
    public interface ILazySocketClient
    {
        Task<ILazySocketMessageChannel> TryGetMessageChannelAsync(ulong channelId);

        Task<ILazySocketUser> TryGetUserAsync(ulong userId);
    }

    public sealed class LazySocketUser : ILazySocketUser
    {
        readonly SocketUser _user;

        public LazySocketUser(SocketUser user)
        {
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        public Task<ulong> GetIdAsync()
        {
            return Task.FromResult(_user.Id);
        }

        public Task<bool> GetIsBotAsync()
        {
            return Task.FromResult(_user.IsBot);
        }

        public Task<string> GetMentionAsync()
        {
            return Task.FromResult(_user.Mention);
        }

        public Task<string> GetUsernameAsync()
        {
            return Task.FromResult(_user.Username);
        }
    }

    public sealed class LazySocketMessageChannel : ILazySocketMessageChannel
    {
        readonly ISocketMessageChannel _channel;

        public LazySocketMessageChannel(ISocketMessageChannel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        }

        public Task<ulong> GetIdAsync()
        {
            return Task.FromResult(_channel.Id);
        }

        public Task<string> GetNameAsync()
        {
            return Task.FromResult(_channel.Name);
        }

        public async Task<RestUserMessage> SendMessageAsync(string text)
        {
            return await _channel.SendMessageAsync(text);
        }
    }

    public sealed class LazySocketMessage : ILazySocketMessage
    {
        readonly SocketMessage _message;

        public LazySocketMessage(SocketMessage message)
        {
            _message = message ?? throw new ArgumentNullException(nameof(message));
        }

        public Task<ILazySocketUser> GetAuthorAsync()
        {
            return Task.FromResult<ILazySocketUser>(new LazySocketUser(_message.Author));
        }

        public Task<ILazySocketMessageChannel> GetChannelAsync()
        {
            return Task.FromResult<ILazySocketMessageChannel>(new LazySocketMessageChannel(_message.Channel));
        }

        public Task<string> GetContentAsync()
        {
            return Task.FromResult(_message.Content);
        }

        public Task<IReadOnlyCollection<ILazySocketUser>> GetMentionedUsersAsync()
        {
            IReadOnlyCollection<ILazySocketUser> result = 
                _message.MentionedUsers
                .Select(user => new LazySocketUser(user))
                .ToArray()
                .ToReadOnly();
            return Task.FromResult(result);
        }
    }

    public sealed class LazySocketClient : ILazySocketClient
    {
        // _client.Readyイベントがfireされていないと、存在するチャンネルに対してclient.GetChannelを呼んでもnullを返したりやclient.Guildsが空だったりする。
        // なので、_clientはReadyイベントがfireされた（もしくはそれに近い）状態になっているべき
        readonly DiscordSocketClient _client;
        
        // このコンストラクタに渡すDiscordSocketClientは、client.Readyイベントがfireされた状態で渡すこと！！
        public LazySocketClient(DiscordSocketClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public Task<ILazySocketMessageChannel> TryGetMessageChannelAsync(ulong channelId)
        {
            var channel = _client.GetChannel(channelId) as ISocketMessageChannel;
            if (channel == null)
            {
                return null;
            }
            ILazySocketMessageChannel result = new LazySocketMessageChannel(channel);
            return Task.FromResult(result);
        }

        public Task<ILazySocketUser> TryGetUserAsync(ulong userId)
        {
            var user = _client.GetUser(userId);
            if (user == null)
            {
                return null;
            }
            ILazySocketUser result = new LazySocketUser(user);
            return Task.FromResult(result);
        }
    }
}
