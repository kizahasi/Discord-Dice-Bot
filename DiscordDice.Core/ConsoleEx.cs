using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordDice
{
    // 色使いがダサかったり統一性がないかも…
    public static class ConsoleEx
    {
        public static void WriteCaution(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("Caution: ");
            Console.ResetColor();
            Console.WriteLine(message);
        }

        public static void WriteError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("Error: ");
            Console.ResetColor();
            Console.WriteLine(message);
        }

        public static void WriteReceivedMessage(string message)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        public static void WriteSentMessage(string channel, string message)
        {
            Console.Write($"{channel}: ");
            Console.BackgroundColor = ConsoleColor.Blue;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }
}
