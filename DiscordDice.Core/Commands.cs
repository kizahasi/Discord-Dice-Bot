using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Reactive.Subjects;
using System.Reactive.Linq;
using System.Reactive;
using System.Threading.Tasks;

namespace DiscordDice.Commands
{
    ///<summary>例えば scan-start などのようなコマンドを示します。</summary>
    // 1d100 のようなロールは処理と文法が特殊でありなおかつ乱数以外に内部状態を持たないので Command クラスを用いない
    internal abstract class Command
    {
        public Command()
        {
            // HACK: コンストラクタで例外を投げるのはイマイチだし、そもそも Keys に参照透過性がなかったら意味がない。かといって継承が適切かどうかをチェックする方法が他に思いつかない…。そもそもこのチェックが必要かどうかを見直すべきでは。
            Guard();
        }

        private void Guard()
        {
            if (Body == null)
            {
                throw new DiscordDiceException($"{nameof(Body)} == null");
            }

            if (Options == null)
            {
                return;
            }

            var hasNoDuplicateKeys =
                Options
                .Where(option => option != null)
                .Select(option => option.Keys)
                .Where(keys => keys != null)
                .SelectMany(keys => keys)
                .Where(key => key != null)
                .GroupBy(key => key)
                .All(group => group.Count() == 1);
            if (!hasNoDuplicateKeys)
            {
                throw new DiscordDiceException($"{Body} に重複したオプションがあります。");
            }
        }

        public abstract string Body { get; }

        public abstract bool NeedMentioned { get; }

        public virtual IReadOnlyCollection<string> BodyAliases { get => null; }

        public IEnumerable<string> GetBodies()
        {
            return
                new[] { Body }
                .Concat(BodyAliases ?? Enumerable.Empty<string>())
                .Where(body => body != null);
        }

        public virtual IReadOnlyCollection<CommandOption> Options
        {
            get
            {
                return null;
            }
        }

        public bool CanInvoke(RawCommand command)
        {
            if (command == null)
            {
                return false;
            }
            if (NeedMentioned && !command.IsMentioned)
            {
                return false;
            }
            return GetBodies().Contains(command.Body);
        }

        protected abstract Task<Response> InvokeCoreAsync(ILazySocketClient client, ILazySocketMessageChannel channel, ILazySocketUser user);

        public async Task<Response> InvokeAsync(RawCommand command, ILazySocketClient client, ulong channelId, ulong userId)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            var channel = await client.TryGetMessageChannelAsync(channelId);
            var user = await client.TryGetUserAsync(userId);

            if (channel == null || user == null)
            {
                return Response.None;
            }
            if (command == null)
            {
                return Response.None;
            }
            if (!CanInvoke(command))
            {
                return Response.None;
            }

            if (command.HasDuplicateOption)
            {
                return await Response.TryCreateCautionAsync(client, "同名のオプションが複数あります。", channelId, userId);
            }
            var usedCommand = new HashSet<CommandOption>(new RefereneEqualsEqualityComparer<CommandOption>());
            foreach (var pair in command.Options)
            {
                var found = (Options ?? new CommandOption[] { }).FirstOrDefault(option => option.Keys.Contains(pair.name));
                if (found == null)
                {
                    return await Response.TryCreateCautionAsync(client, Texts.Error.Commands.Options.ContainsNotSupportedOption(pair.name), channelId, userId) ?? Response.None;
                }
                if (!usedCommand.Add(found))
                {
                    return await Response.TryCreateCautionAsync(client, "同じ意味のオプションが複数あります。", channelId, userId);
                }
                var result = found.SetValue(pair.name, pair.values);
                if (!result.HasValue)
                {
                    return await Response.TryCreateCautionAsync(client, result.Error ?? $"{pair.name} の値としてサポートしていない形式が使われています。", channelId, userId);
                }
            }
            return await InvokeCoreAsync(client, channel, user) ?? Response.None;
        }

        // null の場合はヘルプに含まれない。
        public virtual Help Help { get => null; }
    }

    public sealed class Help
    {
        public Help(string commandName, string text)
        {
            CommandName = commandName ?? throw new ArgumentNullException(nameof(commandName));
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        public string CommandName { get; }

        public string Text { get; }
    }

    internal static class CommandActions
    {
        public static async Task<Response> Help(ILazySocketClient client, ILazySocketMessageChannel channel, string helpMessage)
            => await Response.TryCreateSayAsync(client, $"{helpMessage}", await channel.GetIdAsync());

        public static async Task<Response> Version(ILazySocketClient client, ILazySocketMessageChannel channel)
            => await Response.TryCreateSayAsync(client, $"version {Texts.Version}", await channel.GetIdAsync());

        public static async Task<Response> Changelog(ILazySocketClient client, ILazySocketMessageChannel channel)
        {
            var text = @"```
更新履歴

# v0.3.8(beta) - 2019/05/01

- 終了後のscanを!scan-showした場合にも""途中経過""と表示されてしまっていた問題を修正

# v0.3.7(beta) - 2019/04/30

- メモリリークが起こっていたと思われる部分を修正
- メンションなしでも、例えば!scan-startのように!を最初につけることでもコマンドが実行できるようになった
- !scan-showの--shuffledは--shuffleとも書けるようになった

# v0.3.5(beta) - 2019/04/25

- Discord.NETが切断された後に再接続された際、それ以降ダイスなどの結果が多重に書き込まれることがあった問題を修正

# v0.3.4(beta) - 2019/04/23

- scanができなかった問題を修正

# v0.3.3(beta) - 2019/04/21

- 乱数をメルセンヌツイスタに変更
- n面ダイスを振る際、nの値が大きすぎるとオーバーフローする問題を修正

# v0.3.2(beta) - 2019/04/20

- (開発者にのみ影響がある修正)

# v0.3.1(beta) - 2019/04/20

- 式にプラスやマイナスを含むダイスロールをした際、結果のテキストにプラスやマイナスが多重に表示されてしまう問題を修正

# v0.3.0(beta) - 2019/04/20

- 全角文字を含むダイスロールをサポート
- Scanが削除されるまでの時間を30分から2時間に延長
- BOTの止まる頻度がおそらく低下
```";
            return await Response.TryCreateSayAsync(client, text, await channel.GetIdAsync());
        }


    }

    internal sealed class HelpCommand : Command
    {
        public override string Body => "!help";

        public override bool NeedMentioned => false;

        // ここにヘルプメッセージをセットして使う。このような仕様になってしまったのは、「HelpCommand にはヘルプメッセージが必要」と「ヘルプメッセージ作成には全てのコマンドが必要」の循環参照のせい。
        public Func<string> HelpMessage { get; set; }

        protected override async Task<Response> InvokeCoreAsync(ILazySocketClient client, ILazySocketMessageChannel channel, ILazySocketUser user)
            => await CommandActions.Help(client, channel, HelpMessage());

        public override Help Help => new Help("help", $"{Texts.BotName} のヘルプを表示します。");
    }

    internal sealed class LegacyHelpCommand : Command
    {
        public override string Body => "help";
        public override IReadOnlyCollection<string> BodyAliases => new[] { "-h", "--help" };

        public override bool NeedMentioned => true;

        // ここにヘルプメッセージをセットして使う。このような仕様になってしまったのは、「HelpCommand にはヘルプメッセージが必要」と「ヘルプメッセージ作成には全てのコマンドが必要」の循環参照のせい。
        public Func<string> HelpMessage { get; set; }

        protected override async Task<Response> InvokeCoreAsync(ILazySocketClient client, ILazySocketMessageChannel channel, ILazySocketUser user)
        => await CommandActions.Help(client, channel, HelpMessage());
    }

    internal sealed class VersionCommand : Command
    {
        public override string Body => "!version";

        public override bool NeedMentioned => false;

        protected override async Task<Response> InvokeCoreAsync(ILazySocketClient client, ILazySocketMessageChannel channel, ILazySocketUser user)
            => await CommandActions.Version(client, channel);

        public override Help Help => new Help("version", $"{Texts.BotName} のバージョンを表示します。");
    }

    internal sealed class LegacyVersionCommand : Command
    {
        public override string Body => "version";

        public override IReadOnlyCollection<string> BodyAliases => new[] { "-v", "--version" };

        public override bool NeedMentioned => true;

        protected override async Task<Response> InvokeCoreAsync(ILazySocketClient client, ILazySocketMessageChannel channel, ILazySocketUser user)
            => await CommandActions.Version(client, channel);
    }

    internal sealed class ChangelogCommand : Command
    {
        public override string Body => "!changelog";

        public override bool NeedMentioned => false;

        protected override async Task<Response> InvokeCoreAsync(ILazySocketClient client, ILazySocketMessageChannel channel, ILazySocketUser user)
            => await CommandActions.Changelog(client, channel);

        public override Help Help => new Help("changelog", $"{Texts.BotName} の更新履歴を表示します。");
    }

    internal sealed class LegacyChangelogCommand : Command
    {
        public override string Body => "changelog";

        public override bool NeedMentioned => true;

        protected override async Task<Response> InvokeCoreAsync(ILazySocketClient client, ILazySocketMessageChannel channel, ILazySocketUser user)
            => await CommandActions.Changelog(client, channel);
    }

    internal abstract class ScanStartCommandBase : Command
    {
        readonly BasicMachines.ScanMachine _scanMachine;
        readonly DiceOption _diceOption = new DiceOption();
        readonly MaxSizeOption _maxSizeOption = new MaxSizeOption();
        readonly NoProgressOption _noProgressOption = new NoProgressOption();
        readonly ForceOption _forceOption = new ForceOption();

        public ScanStartCommandBase(BasicMachines.ScanMachine scanMachine)
        {
            _scanMachine = scanMachine ?? throw new ArgumentNullException(nameof(scanMachine));
        }

        public override IReadOnlyCollection<CommandOption> Options => new CommandOption[] { _diceOption, _maxSizeOption, _noProgressOption, _forceOption };

        protected override async Task<Response> InvokeCoreAsync(ILazySocketClient client, ILazySocketMessageChannel channel, ILazySocketUser user)
        {
            var dice = _diceOption.Value ?? Expr.Main.Interpret("1d100");
            var maxSize = _maxSizeOption.Value ?? int.MaxValue;
            var noProgress = _noProgressOption.HasOption;
            var force = _forceOption.HasOption;
            await _scanMachine.StartAsync(channel, user, force, dice, maxSize, noProgress);
            return Response.None;
        }

        sealed class DiceOption : CommandOptionWithExprValue
        {
            public override IReadOnlyCollection<string> Keys => new[] { "--dice" };

            public override string OptionInstructionHelpText => "集計対象とするダイス。";

            public override string DefaultOptionValueHelpText => "1d100";
        }

        sealed class MaxSizeOption : CommandOptionWithPositiveIntValue
        {
            public override IReadOnlyCollection<string> Keys => new[] { "--max-size" };

            public override string OptionInstructionHelpText => "集計対象とする人数の上限。";
        }

        sealed class NoProgressOption : CommandOptionWithNoValue
        {
            public override IReadOnlyCollection<string> Keys => new[] { "--no-progress" };

            public override string OptionInstructionHelpText => "途中経過を表示しない。";
        }

        sealed class ForceOption : CommandOptionWithNoValue
        {
            public override IReadOnlyCollection<string> Keys => new[] { "-f", "--force" };

            public override string OptionInstructionHelpText => "集計中でも、強制終了して新たな集計を開始。";
        }
    }

    internal sealed class ScanStartCommand : ScanStartCommandBase
    {
        public ScanStartCommand(BasicMachines.ScanMachine scanMachine) : base(scanMachine)
        {
            
        }

        public override string Body => "!scan-start";

        public override bool NeedMentioned => false;

        private static readonly string helpText = @"次に scan-end コマンドが実行されるまでに振られたダイスを集計します。
scan-end コマンドは scan-start コマンドを実行したユーザーと同一の人物が実行します。
指定されたダイスのみが集計されます。
同じユーザーが指定されたダイスを 2 回以上振った場合、最初に振られたダイスのみが集計対象となります。
ダイスの値が同じユーザーが複数いる場合、乱数によって自動的にタイブレークが行われます。
scan-end コマンドが実行されないまま長い時間が経過した場合、集計は自動的にキャンセルされます。";

        public override Help Help => new Help("scan-start", helpText);
    }

    internal sealed class LegacyScanStartCommand : ScanStartCommandBase
    {
        public LegacyScanStartCommand(BasicMachines.ScanMachine scanMachine) : base(scanMachine)
        {

        }

        public override string Body => "scan-start";

        public override bool NeedMentioned => true;
    }

    internal abstract class ScanShowCommandBase : Command
    {
        readonly BasicMachines.ScanMachine _scanMachine;
        readonly ShuffledOption _shuffledOption = new ShuffledOption();

        public ScanShowCommandBase(BasicMachines.ScanMachine scanMachine)
        {
            _scanMachine = scanMachine ?? throw new ArgumentNullException(nameof(scanMachine));
        }

        public override IReadOnlyCollection<CommandOption> Options => new CommandOption[] { _shuffledOption };

        protected override async Task<Response> InvokeCoreAsync(ILazySocketClient client, ILazySocketMessageChannel channel, ILazySocketUser user)
        {
            await _scanMachine.GetLatestProgressAsync(channel, user, _shuffledOption.HasOption);
            return Response.None;
        }

        sealed class ShuffledOption : CommandOptionWithNoValue
        {
            public override IReadOnlyCollection<string> Keys => new[] { "--shuffle", "--shuffled" };

            public override string OptionInstructionHelpText => "シャッフルした状態で表示。BOT内部に保存されている集計データそのものはシャッフルされません。";
        }
    }

    internal sealed class ScanShowCommand : ScanShowCommandBase
    {
        public ScanShowCommand(BasicMachines.ScanMachine scanMachine) : base(scanMachine)
        {

        }

        public override string Body => "!scan-show";

        public override bool NeedMentioned => false;

        public override Help Help => new Help("scan-show", "自身が行っているもしくは行ったダイスの集計の途中経過を表示します。");
    }

    internal sealed class LegacyScanShowCommand : ScanShowCommandBase
    {
        public LegacyScanShowCommand(BasicMachines.ScanMachine scanMachine) : base(scanMachine)
        {

        }

        public override string Body => "scan-show";

        public override bool NeedMentioned => true;
    }

    internal abstract class ScanEndCommandBase : Command
    {
        readonly BasicMachines.ScanMachine _scanMachine;
        readonly NoResultOption _noResultOption = new NoResultOption();

        public ScanEndCommandBase(BasicMachines.ScanMachine scanMachine)
        {
            _scanMachine = scanMachine ?? throw new ArgumentNullException(nameof(scanMachine));
        }

        public override IReadOnlyCollection<CommandOption> Options => new CommandOption[] { _noResultOption };

        protected override async Task<Response> InvokeCoreAsync(ILazySocketClient client, ILazySocketMessageChannel channel, ILazySocketUser user)
        {
            var noResult = _noResultOption.HasOption;

            if (noResult)
            {
                await _scanMachine.AbortAsync(await channel.GetIdAsync(), await user.GetIdAsync());
            }
            else
            {
                await _scanMachine.EndAsync(await channel.GetIdAsync(), await user.GetIdAsync());
            }
            return Response.None;
        }

        sealed class NoResultOption : CommandOptionWithNoValue
        {
            public override IReadOnlyCollection<string> Keys => new[] { "--no-result" };

            public override string OptionInstructionHelpText => "集計結果を表示しない。";
        }
    }

    internal sealed class ScanEndCommand : ScanEndCommandBase
    {
        public ScanEndCommand(BasicMachines.ScanMachine scanMachine) : base(scanMachine)
        {

        }

        public override string Body => "!scan-end";

        public override bool NeedMentioned => false;

        public override Help Help => new Help("scan-end", @"自身が行っているダイスの集計を終了します。");
    }

    internal sealed class LegacyScanEndCommand : ScanEndCommandBase
    {
        public LegacyScanEndCommand(BasicMachines.ScanMachine scanMachine) : base(scanMachine)
        {

        }

        public override string Body => "scan-end";

        public override bool NeedMentioned => true;
    }
}
