using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DiscordDice
{
    internal static class Texts
    {
        public static readonly string BotName = "DiceBot";
        public static class Error
        {
            public static readonly string SocketUserNotFound = $"{nameof(SocketUser)} が見つかりませんでした。";
            public static readonly string SocketMessageChannelNotFound = $"{nameof(ISocketMessageChannel)} が見つかりませんでした。";
            public static readonly string SocketMessageNotFound = $"{nameof(SocketMessage)} が見つかりませんでした。";

            public static class Commands
            {
                public static string NotFound(string command) => $"{command} コマンドは存在しません。";

                public static class Options
                {
                    public static readonly string OptionIsNotSupported = "このコマンドでオプションを指定することはできません。";
                    public static string ContainsNotSupportedOption(string notSupportedOptionKey) => $"{notSupportedOptionKey} オプションはサポートされていません。";
                    public static string ValueIsNotSupported(string optionKey) => $"{optionKey} オプションには値を付けることはできません。";
                    public static string NotFoundValue(string optionKey) => $"{optionKey} オプションは値が必要です。";
                    public static string InvalidValue(string optionKey, string optionValue) => $"{optionKey} オプションの値として {optionValue} が使われましたが、サポートされていない形式です。";
                }
            }
        }

        public static readonly string Version = "0.3.0(beta)";
    }
}
