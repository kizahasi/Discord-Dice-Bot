using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Reactive;

namespace DiscordDice.Commands
{
    // 文字列の状態のコマンド。
    internal sealed class RawCommand
    {
        private RawCommand()
        {

        }

        public string Body { get => Bodies.Count == 1 ? Bodies[0] : null; }

        // non-null
        public IReadOnlyList<string> Bodies { get; private set; }

        // non-null
        // nameとvaluesとvaluesの要素も全てnon-null
        public IReadOnlyList<(string name, IReadOnlyList<string> values)> Options { get; private set; }

        // 重複しているオプションがあるときはtrue、全て独立しているときはfalse
        public bool HasDuplicateOption
        {
            get => Options.Select(o => o.name).Distinct().Count() != Options.Count;
        }

        public bool HasMultipleOptionValue
        {
            get => Options.Any(o => o.values.Count >= 2);
        }

        public bool HasMentions { get; private set; }

        public bool IsMentioned { get; private set; }

        // 引数の例1: ["command", "-a", "-b", "param"]
        // 引数の例2: ["--help", "-a"]
        // "@DiceBot" や "<@1234567890>" のような部分は取り除いて渡す
        public static RawCommand Create(IEnumerable<string> phrases, bool hasMentions, bool isMentioned)
        {
            if (phrases == null) throw new ArgumentNullException(nameof(phrases));

            var cleanPhrases =
                phrases
                .Where(phrase => phrase != null)
                .Where(phrase => !string.IsNullOrWhiteSpace(phrase));

            /*
+----------------------------------------+----------------+-----------------------------------+
| input(phrases)                         | output(bodies) | output(options)                   |
+========================================+================+===================================+
| ["command"]                            | ["command"]    | []                                |
+----------------------------------------+----------------+-----------------------------------+
| ["a", "b"]                             | ["a", "b"]     | []                                |
+----------------------------------------+----------------+-----------------------------------+
| []                                     | []             | []                                |
+----------------------------------------+----------------+-----------------------------------+
| ["-a"]                                 | ["-a"]         | []                                |
+----------------------------------------+----------------+-----------------------------------+
| ["--help", "-a"]                       | ["--help"]     | [ ("-a", []) ]                    |
+----------------------------------------+----------------+-----------------------------------+
| ["a", "b", "-a"]                       | ["a", "b"]     | [ ("-a", []) ]                    |
+----------------------------------------+----------------+-----------------------------------+
| ["command", "-a", "-b", "param"]       | ["command"]    | [ ("-a", []), ("-b", ["param"]) ] |
+----------------------------------------+----------------+-----------------------------------+
| ["command", "-a", "param0" , "param1"] | ["command"]    | [ ("-a", ["param0", "param1"]) ]  |
+----------------------------------------+----------------+-----------------------------------+
            */
            var isFirst = true;
            var bodies = new List<string>();
            var options = new List<(string name, IReadOnlyList<string> values)>();
            string lastOptionName = null;
            var lastOptionValues = new List<string>();
            foreach (var phrase in cleanPhrases)
            {
                if (isFirst)
                {
                    bodies.Add(phrase);
                    isFirst = false;
                    continue;
                }

                if (phrase.FirstOrDefault() == '-')
                {
                    if (lastOptionName == null)
                    {
                        lastOptionName = phrase;
                        continue;
                    }

                    options.Add((lastOptionName, lastOptionValues.ToReadOnly()));
                    lastOptionName = phrase;
                    lastOptionValues = new List<string>();
                    continue;
                }

                if (lastOptionName == null)
                {
                    bodies.Add(phrase);
                    continue;
                }
                lastOptionValues.Add(phrase);
            }
            if (lastOptionName != null)
            {
                options.Add((lastOptionName, lastOptionValues.ToReadOnly()));
            }

            return new RawCommand { Bodies = bodies.ToReadOnly(), Options = options.ToReadOnly(), HasMentions = hasMentions, IsMentioned = isMentioned };
        }

        private static Regex CreateRegex(ulong botCurrentUserId)
        {
            // 楽をするため、@Dicebotの前の文は考慮していない。
            // 例えば「--deathbattle @Yggdrasil @Dicebot」のような文は「?<command>」は空とみなされる。
            return new Regex($@"^\s*<@\!?(?<id>{botCurrentUserId})>(?<command>.*)$");
        }

        // 失敗だがエラーメッセージをユーザーに伝える必要がない場合は null を返す。
        public static async Task<RawCommand> CreateFromSocketMessageOrDefaultAsync(ILazySocketMessage message, ulong botCurrentUserId)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            var isMentioned = false;
            var mentionedUsers = await message.GetMentionedUsersAsync();
            foreach (var mentionedUser in mentionedUsers)
            {
                // @everyone や @here では MentionedUsers に各ユーザーの ID は含まれないようなので、このような単純な処理で OK
                if (await mentionedUser.GetIdAsync() == botCurrentUserId)
                {
                    isMentioned = true;
                }
            }
            if (isMentioned && mentionedUsers.Count >= 2)
            {
                return null;
            }
            if (!isMentioned && mentionedUsers.Count >= 1)
            {
                return null;
            }
            if (!isMentioned)
            {
                var phrases = (await message.GetContentAsync()).Split(new char[] { '\r', '\n', ' ' });
                return Create(phrases, mentionedUsers.Any(), false);
            }

            var m = CreateRegex(botCurrentUserId).Match(await message.GetContentAsync());
            if (!m.Success)
            {
                //return Result<RawCommand, string>.CreateError("コマンドがありません。");
                return null;
            }
            var command = m.Groups["command"].Value;
            {
                var phrases = command.Split(new char[] { '\r', '\n', ' ' });
                return Create(phrases, mentionedUsers.Any(), true);
            }
        }
    }

    internal abstract class CommandOption
    {
        public CommandOption()
        {
            // HACK: コンストラクタで例外を投げるのはイマイチだし、そもそも Keys に参照透過性がなかったら意味がない。かといって継承が適切かどうかをチェックする方法が他に思いつかない…。そもそもこのチェックが必要かどうかを見直すべきでは。
            Guard();
        }

        private void Guard()
        {
            if (Keys == null || Keys.Count == 0 || Keys.Contains(null))
            {
                throw new DiscordDiceException($"{nameof(CommandOption)}.{nameof(Keys)} に問題があります。");
            }
        }

        public abstract IReadOnlyCollection<string> Keys { get; }

        // セットする Value は、継承先にそれぞれ設ける。理由は、Value の型に制約を持たせたくないのと、Value を持たないケースがあるから。
        // このため、SetValue は副作用があることが多い。インスタンスを不必要に使いまわさないよう注意。
        //
        // デフォルトのエラーメッセージを用いたエラーにしたい場合は Result.CreateError(null) を返す。
        public abstract Result<Unit, string> SetValue(string rawKey, IReadOnlyList<string> optionValues);

        public abstract string OptionInstructionHelpText { get; }
        public virtual string OptionValueHelpText { get => null; }
        public virtual string DefaultOptionValueHelpText { get => null; }
    }

    internal abstract class CommandOptionWithNoValue : CommandOption
    {
        public bool HasOption { get; private set; }

        public sealed override Result<Unit, string> SetValue(string rawKey, IReadOnlyList<string> optionValues)
        {
            if (optionValues.Any())
            {
                return Result<Unit, string>.CreateError(Texts.Error.Commands.Options.ValueIsNotSupported(rawKey));
            }
            HasOption = true;
            return Result<Unit, string>.CreateValue(Unit.Default);
        }

        public sealed override string OptionValueHelpText => null;
        public sealed override string DefaultOptionValueHelpText => null;
    }

    internal abstract class CommandOptionWithExprValue : CommandOption
    {
        public Expr.Main Value { get; private set; }

        public sealed override Result<Unit, string> SetValue(string rawKey, IReadOnlyList<string> optionValues)
        {
            if (optionValues.Count == 0)
            {
                return Result<Unit, string>.CreateError(Texts.Error.Commands.Options.NotFoundValue(rawKey));
            }
            if (optionValues.Count >= 2)
            {
                return Result<Unit, string>.CreateError(Texts.Error.Commands.Options.MultipleValues(rawKey));
            }
            var optionValue = optionValues.First();

            var d = Expr.Main.Interpret(optionValue);
            if (!d.IsValid)
            {
                return Result<Unit, string>.CreateError(Texts.Error.Commands.Options.InvalidValue(rawKey, optionValue));
            }
            Value = d;
            return Result<Unit, string>.CreateValue(Unit.Default);
        }

        public sealed override string OptionValueHelpText => "<expr>";
    }

    internal abstract class CommandOptionWithPositiveIntValue : CommandOption
    {
        public int? Value { get; private set; }

        public sealed override Result<Unit, string> SetValue(string rawKey, IReadOnlyList<string> optionValues)
        {
            if (optionValues.Count == 0)
            {
                return Result<Unit, string>.CreateError(Texts.Error.Commands.Options.NotFoundValue(rawKey));
            }
            if (optionValues.Count >= 2)
            {
                return Result<Unit, string>.CreateError(Texts.Error.Commands.Options.MultipleValues(rawKey));
            }
            var optionValue = optionValues.First();

            if (int.TryParse(optionValue, out var parsedOptionValue) && parsedOptionValue >= 0)
            {
                Value = parsedOptionValue;
                return Result<Unit, string>.CreateValue(Unit.Default);
                
            }
            return Result<Unit, string>.CreateError(Texts.Error.Commands.Options.InvalidValue(rawKey, optionValue));
        }

        public sealed override string OptionValueHelpText => "<n>";
    }
}
