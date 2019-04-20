using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace DiscordDice.Models
{
    internal class User
    {
        // DiscordにおけるユーザーのID
        // 本来はulongだが現在のEntity Framework Coreは対応してないっぽいのでstring
        public string ID { get; set; }

        public string Username { get; set; }
    }

    internal class Scan
    {
        public string ID { get; set; }

        // DiscordにおけるチャンネルのID
        // 本来はulongだが現在のEntity Framework Coreは対応してないっぽいのでstring
        public string ChannelID { get; set; }

        public string ScanStartedUserID { get; set; }

        public Expr.Main Expr { get; set; }

        public int MaxSize { get; set; }

        public bool NoProgress { get; set; }

        public User ScanStartedUser { get; set; }

        public DateTimeOffset StartedAt { get; set; }

        // もうスキャンが終わっていたらtrue、現在も続行中ならfalse
        public bool IsArchived { get; set; }

        public ICollection<ScanRoll> ScanRolls { get; set; }
    }

    internal class ScanRoll
    {
        public string UserID { get; set; }

        public User User { get; set; }

        public string ScanID { get; set; }

        public Scan Scan { get; set; }

        // 振って出たダイスの値
        public int Value { get; set; }

        // Valueが同じだったときのタイブレーカー。
        // a > b == trueのときはaのほうが順位が上、という感じで大小比較する。
        // 通常はGuidを使う。
        public string ValueTieBreaker { get; set; }
    }
}
