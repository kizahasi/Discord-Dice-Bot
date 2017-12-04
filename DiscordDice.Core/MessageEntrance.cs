﻿using DiscordDice.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

/*
[MessageEntrance] ──────────────メソッド呼び出し───────────────→ [BasicMachine]
        |                                                              ↑
        ───Discord のメッセージを渡す───→ [Command] ───メソッド呼び出し──┘


[MessageEntrance] ←────────IObservable<Response>で通知───────── [BasicMachine] 
*/
namespace DiscordDice
{
    public sealed class MessageEntrance
    {
        readonly Subject<Response> _manualResponseSent = new Subject<Response>();
        readonly BasicMachines.AllInstances _basicMachines;

        public MessageEntrance(ITime time)
        {
            if (time == null) throw new ArgumentNullException(nameof(time));

            _basicMachines = new BasicMachines.AllInstances(time);
            
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
            await _basicMachines.Scan.SetDiceAsync(channel, author, executed);
            var result = Response.CreateSay($"{await author.GetMentionAsync()} {executed.Message}", await message.GetChannelAsync());
            return (result, false);
        }

        public async Task OnNextAsync(ILazySocketMessage message, ulong botCurrentUserId)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            var content = await message.GetContentAsync();
            //ConsoleEx.WriteReceivedMessage(content);

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
            if(!executesCommand)
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
                _manualResponseSent.OnNext(Response.CreateCaution(rawCommand.Error, channel, author));
                return;
            }

            var allCommands = CreateAllCommands();
            if (allCommands.TryGetValue(rawCommand.Value.Body, out var command))
            {
                _manualResponseSent.OnNext(await command.InvokeAsync(rawCommand.Value, channel, author));
                return;
            }
            _manualResponseSent.OnNext(Response.CreateCaution(Texts.Error.Commands.NotFound(rawCommand.Value.Body), channel, author));
        }
    }
}
