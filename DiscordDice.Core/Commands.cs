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
            return GetBodies().Contains(command.Body);
        }

        protected abstract Task<Response> InvokeCoreAsync(ILazySocketClient client, ILazySocketMessageChannel channel, ILazySocketUser user);

        public async Task<Response> InvokeAsync(RawCommand command, ILazySocketClient client, ulong channelId, ulong userId)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            var channel = await client.TryGetMessageChannelAsync(channelId);
            var user = await client.TryGetUserAsync(userId);

            if(channel == null || user == null)
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

            var usedCommand = new HashSet<CommandOption>(new RefereneEqualsEqualityComparer<CommandOption>());
            foreach (var pair in command.Options)
            {
                var found = (Options ?? new CommandOption[] { }).FirstOrDefault(option => option.Keys.Contains(pair.Key));
                if (found == null)
                {
                    return await Response.TryCreateCautionAsync(client, Texts.Error.Commands.Options.ContainsNotSupportedOption(pair.Key), channelId, userId) ?? Response.None;
                }
                if(!usedCommand.Add(found))
                {
                    return await Response.TryCreateCautionAsync(client, "同じ意味のオプションが複数あります。", channelId, userId);
                }
                var result = found.SetValue(pair.Key, pair.Value);
                if (!result.HasValue)
                {
                    return await Response.TryCreateCautionAsync(client, result.Error ?? $"{pair.Key} の値としてサポートしていない形式が使われています。", channelId, userId);
                }
            }
            return await InvokeCoreAsync(client, channel, user) ?? Response.None;
        }

        // null の場合はヘルプに含まれない。
        public abstract string HelpText { get; }
    }

    internal sealed class HelpCommand : Command
    {
        public override string Body => "help";
        public override IReadOnlyCollection<string> BodyAliases => new[] { "-h", "--help" };

        // ここにヘルプメッセージをセットして使う。このような仕様になってしまったのは、「HelpCommand にはヘルプメッセージが必要」と「ヘルプメッセージ作成には全てのコマンドが必要」の循環参照のせい。
        public Func<string> HelpMessage { get; set; }

        protected override async Task<Response> InvokeCoreAsync(ILazySocketClient client, ILazySocketMessageChannel channel, ILazySocketUser user)
        {
            return await Response.TryCreateSayAsync(client, $"```{HelpMessage()}```", await channel.GetIdAsync());
        }

        public override string HelpText => $"{Texts.BotName} のヘルプを表示します。";
    }

    internal sealed class VersionCommand : Command
    {
        public override string Body => "version";
        public override IReadOnlyCollection<string> BodyAliases => new[] { "-v", "--version" };

        protected override async Task<Response> InvokeCoreAsync(ILazySocketClient client, ILazySocketMessageChannel channel, ILazySocketUser user)
        {
            return await Response.TryCreateSayAsync(client, $"version {Texts.Version}", await channel.GetIdAsync());
        }

        public override string HelpText => $"{Texts.BotName} のバージョンを表示します。";
    }

    internal sealed class ScanStartCommand : Command
    {
        readonly BasicMachines.ScanMachine _scanMachine;
        readonly DiceOption _diceOption = new DiceOption();
        readonly MaxSizeOption _maxSizeOption = new MaxSizeOption();
        readonly NoProgressOption _noProgressOption = new NoProgressOption();
        readonly ForceOption _forceOption = new ForceOption();

        public ScanStartCommand(BasicMachines.ScanMachine scanMachine)
        {
            _scanMachine = scanMachine ?? throw new ArgumentNullException(nameof(scanMachine));
        }

        public override string Body => "scan-start";

        public override IReadOnlyCollection<CommandOption> Options => new CommandOption[] { _diceOption, _maxSizeOption, _noProgressOption, _forceOption };

        public override string HelpText => @"次に scan-end コマンドが実行されるまでに振られたダイスを集計します。
scan-end コマンドは scan-start コマンドを実行したユーザーと同一の人物が実行します。
指定されたダイスのみが集計されます。
同じユーザーが指定されたダイスを 2 回以上振った場合、最初に振られたダイスのみが集計対象となります。
ダイスの値が同じユーザーが複数いる場合、乱数によって自動的にタイブレークが行われます。
scan-end コマンドが実行されないまま長い時間が経過した場合、集計は自動的にキャンセルされます。";

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

    internal sealed class ScanShowCommand : Command
    {
        readonly BasicMachines.ScanMachine _scanMachine;
        readonly ShuffledOption _shuffledOption = new ShuffledOption();

        public ScanShowCommand(BasicMachines.ScanMachine scanMachine)
        {
            _scanMachine = scanMachine ?? throw new ArgumentNullException(nameof(scanMachine));
        }

        public override string Body => "scan-show";

        public override IReadOnlyCollection<CommandOption> Options => new CommandOption[] { _shuffledOption };

        public override string HelpText => "自身が行っているもしくは行ったダイスの集計の途中経過を表示します。";

        protected override async Task<Response> InvokeCoreAsync(ILazySocketClient client, ILazySocketMessageChannel channel, ILazySocketUser user)
        {
            await _scanMachine.GetLatestProgressAsync(channel, user, _shuffledOption.HasOption);
            return Response.None;
        }

        sealed class ShuffledOption : CommandOptionWithNoValue
        {
            public override IReadOnlyCollection<string> Keys => new[] { "--shuffled" };

            public override string OptionInstructionHelpText => "シャッフルした状態で表示。BOT内部に保存されている集計データそのものはシャッフルされません。";
        }
    }

    internal sealed class ScanEndCommand : Command
    {
        readonly BasicMachines.ScanMachine _scanMachine;
        readonly NoResultOption _noResultOption = new NoResultOption();

        public ScanEndCommand(BasicMachines.ScanMachine scanMachine)
        {
            _scanMachine = scanMachine ?? throw new ArgumentNullException(nameof(scanMachine));
        }

        public override string Body => "scan-end";

        public override IReadOnlyCollection<CommandOption> Options => new CommandOption[] { _noResultOption };

        public override string HelpText => @"自身が行っているダイスの集計を終了します。";

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
}
