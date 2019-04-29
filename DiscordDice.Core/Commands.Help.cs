using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DiscordDice.Commands
{
    internal static class CommandsHelp
    {
        public static string Create(IReadOnlyCollection<Command> commands)
        {
            var resultBuilder = new StringBuilder();
            resultBuilder.Append($@"**DiceBotのヘルプ**

**# ダイスの振り方**
例えば下のように書き込むことでダイスを振ることができます。
`1d6`
`2d100`
`-1d20`
`1d4-2d10+10`

**# コマンド一覧**
[ と ] で囲まれた部分はオプションです。オプションは省略可能であり、順不同です。
");

            foreach (var command in commands)
            {
                var commandText = ToString(command);
                if (commandText != null)
                {
                    resultBuilder.Append($"\r\n{commandText}");
                }
            }

            return resultBuilder.ToString();
        }

        private static string ToString(Command command)
        {
            if (command.Help == null)
            {
                return null;
            }

            var resultBuilder = new StringBuilder($"{command.Help.CommandName}コマンド\r\n```");

            {
                var isFirst = true;
                foreach (var body in command.GetBodies())
                {
                    resultBuilder.Append($"{(isFirst ? "" : "\r\n")}{(command.NeedMentioned ? "<@DiceBot> " : "")}{body}");
                    isFirst = false;
                }
            }

            foreach (var option in command.Options ?? new CommandOption[] { })
            {
                if (option == null) continue;

                resultBuilder.Append(" [");

                // パターン1: [-k | --key]
                // パターン2: [--key value]
                // パターン3: [(-k | --key) value]
                // としたとき、パターン3の処理
                if (option.Keys.Count >= 2 && option.OptionValueHelpText != null)
                {
                    resultBuilder.Append("(");
                }

                var optionString =
                    option.Keys
                    .SelectMany(key => new[] { " | ", key })
                    .Skip(1)
                    .Aggregate(new StringBuilder(), (sb, str) => sb.Append(str));
                resultBuilder.Append(optionString);

                // パターン3の処理
                if (option.Keys.Count >= 2 && option.OptionValueHelpText != null)
                {
                    resultBuilder.Append(")");
                }

                if (option.OptionValueHelpText != null)
                {
                    resultBuilder.Append(" ");
                    resultBuilder.Append(option.OptionValueHelpText);
                }

                resultBuilder.Append("]");
            }

            resultBuilder.Append($"\r\n\r\n説明:\r\n{command.Help.Text}");

            {
                var isFirst = true;
                foreach (var option in command.Options ?? new CommandOption[] { })
                {
                    if (option == null) continue;

                    if (isFirst)
                    {
                        resultBuilder.Append("\r\n\r\nオプション:");
                    }
                    foreach(var key in option.Keys)
                    {
                        resultBuilder.Append($"\r\n{ key } { option.OptionValueHelpText }");
                    }
                    resultBuilder.Append($"\r\n{ option.OptionInstructionHelpText }");
                    isFirst = false;
                }
            }

            resultBuilder.Append("```");
            return resultBuilder.ToString();
        }
    }
}
