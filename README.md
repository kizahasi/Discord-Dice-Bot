# Discord Dice BOT
シンプルな機能を持ったダイス機能をDiscordで使えるようにするためのBOTです。このBOTの特徴として、振られたダイスを記録して出力する集計機能があります。

## BOTユーザーを自分のDiscordサーバーに参加させる
このダイスBOTは無料サーバーで稼働させており、Discordサーバー管理者なら誰でも参加させることができるようになっています。

BOTユーザーを参加させるには、下のリンクをクリックしてください。ただし、そのDiscordサーバーの管理者でないと参加させることはできません。

https://discordapp.com/oauth2/authorize?&client_id=389035105227767817&scope=bot

## コマンド
manual.mdを参照してください。

## サーバーでの稼働方法
**この項目では、サーバーでBOTを動かす方法について説明しています。DiscordでダイスBOTの機能を利用するだけの方は読む必要はありません。**

.NET Coreで動作しますので、.NET Core 2.0以降がインストールできる環境であれば、Windows、Mac、Linuxのいずれでも稼働させることができます。

.NET Coreをインストールします。インストール方法はOSごとに異なります。

"DiscordDice.csproj"があるDiscordDiceディレクトリ("DiscordDice.sln"があるディレクトリではありません)に移動し、"tokens.json"という名前のファイルを作成します。ファイルにBOTのトークンを下のようなフォーマットで入力します。BOTがDEBUG構成で動いているときはdebugキーのトークンが、RELEASE構成で動いているときはreleaseキーのトークンが使われます。よくわからなければ両方に同じトークンを入力してください。

```
{
    "debug": "AbCd.EfgH…",
    "release": "ijkl.MNOP…"
}
```

"DiscordDice.csproj"があるDiscordDiceディレクトリで、`dotnet restore`コマンドを実行します。

"DiscordDice.csproj"があるDiscordDiceディレクトリで、`dotnet run`コマンドを実行することでBOTが起動します。


## License
Licensed under the MIT license. See LICENSE.txt for details.

