using DiscordDice.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

/*
[MessageEntrance] ──────────────メソッド呼び出し───────────────→ [BasicMachine]
        |                                                                                          ↑
        ───Discord のメッセージを渡す────→ [Command] ──────メソッド呼び出し─────┘


[MessageEntrance] ←───────────IObservable<Response>で通知──────────── [BasicMachine] 
*/
namespace DiscordDice
{
    public sealed class MessageEntrance
    {
        readonly ILazySocketClient _client;
        readonly Subject<Response> _manualResponseSent = new Subject<Response>();
        readonly BasicMachines.AllInstances _basicMachines;

        public MessageEntrance(ILazySocketClient client, IConfig config)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            if (config == null) throw new ArgumentNullException(nameof(config));


            _basicMachines = new BasicMachines.AllInstances(client, config);

            ResponseSent = _basicMachines.SentResponse.Merge(_manualResponseSent).Where(r => r != null);
        }

        private IDictionary<string, Command> CreateAllCommands()
        {
            var helpCommand = new HelpCommand();
            var allCommandsByArray =
                new Command[]
                {
                    helpCommand,
                    new VersionCommand(),
                    new ChangelogCommand(),
                    new ScanStartCommand(_basicMachines.Scan),
                    new ScanShowCommand(_basicMachines.Scan),
                    new ScanEndCommand(_basicMachines.Scan),
                };
            helpCommand.HelpMessage = () => CommandsHelp.Create(allCommandsByArray);
            return
                allCommandsByArray
                .SelectMany(command => command.GetBodies().Select(body => new { Key = body, Value = command }))
                .ToDictionary(a => a.Key, a => a.Value);
        }

        public IObservable<Response> ResponseSent { get; }

        // 例えば 1d100 などといった、コマンドを用いない特別な処理。
        private async Task<(Response result, bool executesCommand)> ProcessNonCommandAsync(ILazySocketMessage message, ulong botCurrentUserId)
        {
            var expr = await Expr.Main.InterpretFromLazySocketMessageAsync(message, botCurrentUserId);
            var executed = expr.ExecuteOrDefault();
            if (executed == null || expr.IsConstant)
            {
                return (Response.None, true);
            }
            var channel = await message.GetChannelAsync();
            var author = await message.GetAuthorAsync();
            await _basicMachines.Scan.SetDiceAsync(await channel.GetIdAsync(), await author.GetIdAsync(), await author.GetUsernameAsync() , executed);
            var result = await Response.TryCreateSayAsync(_client, $"{await author.GetMentionAsync()} {executed.Message}", await channel.GetIdAsync()) ?? Response.None;
            return (result, false);
        }

        public async Task ReceiveMessageAsync(ILazySocketMessage message, ulong botCurrentUserId)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            //var content = await message.GetContentAsync();
            //ConsoleEx.WriteReceivedMessage(content);

            await _basicMachines.Scan.TryUpdateScansAsync();

            var author = await message.GetAuthorAsync();
            if (await author.GetIsBotAsync())
            {
                return;
            }

            var (nonCommandResponse, executesCommand) = await ProcessNonCommandAsync(message, botCurrentUserId);
            if (nonCommandResponse.Type != ResponseType.None)
            {
                _manualResponseSent.OnNext(nonCommandResponse);
            }
            if (!executesCommand)
            {
                return;
            }

            var rawCommand = await RawCommand.CreateFromSocketMessageOrDefaultAsync(message, botCurrentUserId);
            if (rawCommand == null)
            {
                return;
            }

            var channel = await message.GetChannelAsync();
            if (!rawCommand.HasValue)
            {
                var response = await Response.TryCreateCautionAsync(_client, rawCommand.Error, await channel.GetIdAsync(), await author.GetIdAsync()) ?? Response.None;
                _manualResponseSent.OnNext(response);
                return;
            }

            var allCommands = CreateAllCommands();
            if (allCommands.TryGetValue(rawCommand.Value.Body, out var command))
            {
                _manualResponseSent.OnNext(await command.InvokeAsync(rawCommand.Value, _client, await channel.GetIdAsync(), await author.GetIdAsync()));
                return;
            }
            {
                var response = await Response.TryCreateCautionAsync(_client, Texts.Error.Commands.NotFound(rawCommand.Value.Body), await channel.GetIdAsync(), await author.GetIdAsync()) ?? Response.None;
                _manualResponseSent.OnNext(response);
            }
        }
    }
}
