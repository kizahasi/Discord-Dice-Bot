﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Discord.Rest;

namespace DiscordDice.Tests
{
    public sealed class TestLazySocketMessageChannel : ILazySocketMessageChannel
    {
        public ulong Id { get; set; }
        public string Name { get; set; }

        Task<ulong> ILazySocketMessageChannel.GetIdAsync() => Task.FromResult(Id);
        Task<string> ILazySocketMessageChannel.GetNameAsync() => Task.FromResult(Name);
        Task<RestUserMessage> ILazySocketMessageChannel.SendMessageAsync(string text) => throw new NotSupportedException(); // これが呼ばれる場合、テスト対象かコードの構造に問題がある

        public static TestLazySocketMessageChannel Default
        {
            get
            {
                return new TestLazySocketMessageChannel { Id = 10, Name = "TestChannel" };
            }
        }
    }

    public sealed class TestLazySocketUser : ILazySocketUser
    {
        public ulong Id { get; set; }
        public bool IsBot { get; set; }
        public string Mention { get; set; }
        public string Username { get; set; }

        Task<ulong> ILazySocketUser.GetIdAsync() => Task.FromResult(Id);
        Task<bool> ILazySocketUser.GetIsBotAsync() => Task.FromResult(IsBot);
        Task<string> ILazySocketUser.GetMentionAsync() => Task.FromResult(Mention);
        Task<string> ILazySocketUser.GetUsernameAsync() => Task.FromResult(Username);

        public static TestLazySocketUser MyBot
        {
            get
            {
                return new TestLazySocketUser { Id = 100, IsBot = true, Mention = "<@100>", Username = "MyBot" };
            }
        }

        public static TestLazySocketUser Author
        {
            get
            {
                return new TestLazySocketUser { Id = 101, IsBot = false, Mention = "<@101>", Username = "Author" };
            }
        }

        public static TestLazySocketUser NonAuthor
        {
            get
            {
                return new TestLazySocketUser { Id = 102, IsBot = false, Mention = "<@102>", Username = "NonAuthor" };
            }
        }

        public static TestLazySocketUser OtherBot
        {
            get
            {
                return new TestLazySocketUser { Id = 103, IsBot = true, Mention = "<@103>", Username = "OtherBot" };
            }
        }
    }

    public sealed class TestLazySocketMessage : ILazySocketMessage
    {
        public ILazySocketUser Author { get; set; }
        public ILazySocketMessageChannel Channel { get; set; }
        public string Content { get; set; }
        public IReadOnlyCollection<ILazySocketUser> MentionedUsers { get; set; }

        Task<ILazySocketUser> ILazySocketMessage.GetAuthorAsync() => Task.FromResult(Author);
        Task<ILazySocketMessageChannel> ILazySocketMessage.GetChannelAsync() => Task.FromResult(Channel);
        Task<string> ILazySocketMessage.GetContentAsync() => Task.FromResult(Content);
        Task<IReadOnlyCollection<ILazySocketUser>> ILazySocketMessage.GetMentionedUsersAsync() => Task.FromResult(MentionedUsers);

        /// <summary>ダイス BOT への Mention を含むメッセージを作成します。</summary>
        public static TestLazySocketMessage CreateMentionedMessage(string context)
        {
            var author = TestLazySocketUser.Author;
            var mentionedUsers = new[] { TestLazySocketUser.MyBot }.ToReadOnly();
            return new TestLazySocketMessage
            {
                Author = author,
                Channel = TestLazySocketMessageChannel.Default,
                Content = $"<@{TestLazySocketUser.MyBot.Id}> {context}",
                MentionedUsers = mentionedUsers
            };
        }

        /// <summary>Mention を含まないメッセージを作成します。</summary>
        public static TestLazySocketMessage CreateNoMentionMessage(string context)
        {
            var author = TestLazySocketUser.Author;
            var mentionedUsers = new TestLazySocketUser[] { }.ToReadOnly();
            return new TestLazySocketMessage
            {
                Author = author,
                Channel = TestLazySocketMessageChannel.Default,
                Content = context,
                MentionedUsers = mentionedUsers
            };
        }

        /// <summary>Mention はあるがダイス BOT への Mention は含まれないメッセージを作成します。</summary>
        public static TestLazySocketMessage CreateNotMentionedMessage(string context)
        {
            var author = TestLazySocketUser.Author;
            var mentionedUsers = new[] { TestLazySocketUser.NonAuthor }.ToReadOnly();
            return new TestLazySocketMessage
            {
                Author = author,
                Channel = TestLazySocketMessageChannel.Default,
                Content = $"<@{TestLazySocketUser.NonAuthor.Id}> {context}",
                MentionedUsers = mentionedUsers
            };
        }

        /// <summary>他の BOT による、Mention を含まないメッセージを作成します。</summary>
        public static TestLazySocketMessage CreateOtherBotMessage(string context)
        {
            var author = TestLazySocketUser.OtherBot;
            var mentionedUsers = new TestLazySocketUser[] { }.ToReadOnly();
            return new TestLazySocketMessage
            {
                Author = author,
                Channel = TestLazySocketMessageChannel.Default,
                Content = $"{context}",
                MentionedUsers = mentionedUsers
            };
        }
    }
}
