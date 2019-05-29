# Discord Dice BOT
シンプルな機能を持ったダイス機能をDiscordで使えるようにするためのBOTです。このBOTの特徴として、振られたダイスを記録して出力する集計機能があります。

![image](https://github.com/rasis-aneki/Discord-Dice-Bot/blob/storage/images/roll+scan.gif)

[![Build Status](https://travis-ci.org/rasis-aneki/Discord-Dice-Bot.svg?branch=master)](https://travis-ci.org/rasis-aneki/Discord-Dice-Bot)

## BOTユーザーを自分のDiscordサーバーに追加する
このダイスBOTは無料サーバーで稼働させており、Discordサーバー管理者なら誰でも追加することができるようになっています。

BOTユーザーを追加するには、下のリンクをクリックしてください。

[**ダイスBOTをDiscordサーバーに追加する**](https://discordapp.com/oauth2/authorize?&client_id=389035105227767817&scope=bot)

## コマンド
[マニュアル](https://github.com/rasis-aneki/Discord-Dice-Bot/blob/storage/manual.md)を参照してください。

## BOTプログラムを動かす
**自前のサーバーでダイスBOTを動かそうと思っている方以外は、この項目を読んでいただく必要はありません。**

.NET Coreで動作しますので、.NET Core 2.0以降がインストールできる環境であれば、Windows、Mac、Linuxのいずれでも稼働させることができます。

まず.NET Coreをインストールしてください。インストール方法はOSごとに異なります。

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
Licensed under the MIT license. See [LICENSE.txt](LICENSE.txt) for details.

