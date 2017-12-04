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

        public string Body { get; private set; }
        // オプションの値がないときは null。
        public IReadOnlyDictionary<string, string> Options { get; private set; }

        // 引数の例1: ["command", "-a", "-b", "param"]
        // 引数の例2: ["--help", "-a"]
        // "@DiceBot" や "<@1234567890>" のような部分は取り除いて渡す
        public static Result<RawCommand, string> Create(IEnumerable<string> phrases)
        {
            if (phrases == null)
            {
                return Result<RawCommand, string>.CreateError("コマンドがありません。");
            }

            var indexedCleanPhrases =
                phrases
                .Where(phrase => phrase != null)
                .Where(phrase => !string.IsNullOrWhiteSpace(phrase))
                .Select((phrase, i) => (phrase, i));

            // ["command", "-a", "-b", "param"] の場合、body == "command", options == [ ("-a", null), ("-b", "param") ] になる。
            // ["--help", "-a"] の場合、body == "--help", options == [ ("-a", null) ] になる。
            // 同じ名前のオプションが複数ある場合、もしくは 1 つのオプションに対してパラメーターが複数ある場合はエラーとみなす。
            string body = null;
            var options = new Dictionary<string, string>();
            string readingOptionName = null;
            foreach (var (phrase, i) in indexedCleanPhrases)
            {
                if (i == 0)
                {
                    body = phrase;
                    continue;
                }

                if (phrase.FirstOrDefault() == '-')
                {
                    if (options.ContainsKey(phrase))
                    {
                        return Result<RawCommand, string>.CreateError("重複しているオプションがあります。");
                    }

                    options[phrase] = null;
                    readingOptionName = phrase;
                    continue;
                }

                if (readingOptionName == null)
                {
                    return Result<RawCommand, string>.CreateError("オプションの値がありますが、それに対応するオプションがありません。");
                }

                if (options.TryGetValue(readingOptionName, out var optionValue) && optionValue != null)
                {
                    return Result<RawCommand, string>.CreateError("オプションの値を複数個指定することはできません。");

                }
                options[readingOptionName] = phrase;
            }

            if (body == null)
            {
                return Result<RawCommand, string>.CreateError("コマンドがありません。");
            }
            return Result<RawCommand, string>.CreateValue(new RawCommand { Body = body, Options = options.ToReadOnly() });
        }

        private static Regex CreateRegex(ulong botCurrentUserId)
        {
            return new Regex($@"^\s*<@\!?(?<id>{botCurrentUserId})>(?<command>.*)$");
        }

        // 失敗だがエラーメッセージをユーザーに伝える必要がない場合は null を返す。
        public static async Task<Result<RawCommand, string>> CreateFromSocketMessageOrDefaultAsync(ILazySocketMessage message, ulong botCurrentUserId)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            var isMentioned = false;
            await
                (await message.GetMentionedUsersAsync())
                .ToAsyncEnumerable()
                .ForEachAsync(async user =>
                {
                    // @everyone や @here では MentionedUsers に各ユーザーの ID は含まれないようなので、このような単純な処理で OK
                    if (await user.GetIdAsync() == botCurrentUserId)
                    {
                        isMentioned = true;
                    }
                });
            if (!isMentioned)
            {
                return null;
            }

            var m = CreateRegex(botCurrentUserId).Match(await message.GetContentAsync());
            if (!m.Success)
            {
                return Result<RawCommand, string>.CreateError("コマンドがありません。");
            }
            var command = m.Groups["command"].Value;
            var phrases = command.Split(new char[] { '\r', '\n', ' ' });
            return Create(phrases);
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
        // オプションの値がないときは optionValue == null。
        // デフォルトのエラーメッセージを用いたエラーにしたい場合は Result.CreateError(null) を返す。
        public abstract Result<Unit, string> SetValue(string optionValue, string rawKey);

        public abstract string OptionInstructionHelpText { get; }
        public virtual string OptionValueHelpText { get => null; }
        public virtual string DefaultOptionValueHelpText { get => null; }
    }

    internal abstract class CommandOptionWithNoValue : CommandOption
    {
        public bool HasOption { get; private set; }

        public sealed override Result<Unit, string> SetValue(string rawKey, string optionValue)
        {
            if (optionValue != null)
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

        public sealed override Result<Unit, string> SetValue(string rawKey, string optionValue)
        {
            if (optionValue == null)
            {
                return Result<Unit, string>.CreateError(Texts.Error.Commands.Options.NotFoundValue(rawKey));
            }
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

        public sealed override Result<Unit, string> SetValue(string rawKey, string optionValue)
        {
            if (optionValue == null)
            {
                return Result<Unit, string>.CreateError(Texts.Error.Commands.Options.NotFoundValue(rawKey));
            }
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
