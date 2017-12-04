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
            resultBuilder.Append($@"DiceBot ヘルプ

# このヘルプの表記について
複数行にわたっているコマンドは、どの行のコマンドも同じ意味であるということを示します。
スペースの部分は 1 個以上の「半角スペース」で記述してください。
[ と ] で囲まれた部分はオプションです。オプションは省略可能であり、順不同です。
<n> は自然数を示します。
<@DiceBot> は BOT へのメンションを示します。


コマンド:
<n>d<n>
<@DiceBot> <n>d<n>

説明:
ダイスを振ることができます。前の数字はダイスの個数を、後の数字はダイスの面の数を示します。

例:
2d100");

            foreach (var command in commands)
            {
                var commandText = ToString(command);
                if (commandText != null)
                {
                    resultBuilder.Append($"\r\n\r\n\r\n{commandText}");
                }
            }

            return resultBuilder.ToString();
        }

        private static string ToString(Command command)
        {
            if (command.HelpText == null)
            {
                return null;
            }

            var resultBuilder = new StringBuilder("コマンド:");

            foreach (var body in command.GetBodies())
            {
                resultBuilder.Append($"\r\n<@DiceBot> {body}");
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

            resultBuilder.Append($"\r\n\r\n説明:\r\n{command.HelpText}");

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

            return resultBuilder.ToString();
        }
    }
}
