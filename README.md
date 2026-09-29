# Claude Usage Tracker for Windows

Claude Code の使用量 (5 時間セッション枠・7 日間の週次枠) を、Windows のタスクトレイに常駐して表示するアプリです。

<p>
  <img src="docs/images/flyout-dark.png" alt="フライアウト (ダーク)" width="300">
  &nbsp;
  <img src="docs/images/flyout-light.png" alt="フライアウト (ライト)" width="300">
</p>

- トレイアイコンのリングで、セッション枠の使用率と状態 (緑・橙・赤) をひと目で確認できる
- クリックで開くフライアウトに、セッション・週次それぞれの使用率とリセットまでの時間を表示
- 使用率が 75% / 90% / 95% に達したときと、セッション枠がリセットされたときに Windows 通知
- ライト / ダーク / システム設定に追従のテーマ切り替え
- Windows 版 Claude Code に加え、WSL 内の Claude Code の認証情報にも対応

## 動作要件

- Windows 10 バージョン 1809 以降、または Windows 11
- [Claude Code](https://docs.anthropic.com/en/docs/claude-code/overview) をインストールし、`claude` でサインイン済みであること (Windows 側か WSL 側のどちらか)
- ビルドには [.NET 10 SDK](https://dotnet.microsoft.com/download) が必要

## ビルドと起動

```powershell
# ビルドして起動
dotnet run --project src/ClaudeUsageTracker.App

# 配布用に発行 (実行には .NET 10 Desktop Runtime が必要)
dotnet publish src/ClaudeUsageTracker.App -c Release -r win-x64 --self-contained false -o publish
```

`publish/ClaudeUsageTracker.App.exe` を実行すると起動します。

## 使い方

### 1. 起動する

起動するとタスクトレイにアイコンが表示されます。初期設定では、起動時にフライアウトも開きます。

アイコンのリングはセッション枠 (5 時間) の使用率を表し、色で状態を示します。

<img src="docs/images/tray-states.png" alt="トレイアイコンの状態: 緑・橙・赤" width="260">

| 色 | 状態 | 目安 |
|---|---|---|
| 緑 | Safe | 余裕あり |
| 橙 | Moderate | このペースだと枠の 70% 以上を使いそう |
| 赤 | Critical | このペースだと枠の 90% 以上を使いそう |

色は、単純な使用率ではなく消費ペースで判定します。セッション枠の経過時間が 15% 以上のときは「今の使用率 ÷ 経過した割合」で枠の終わりの使用率を予測し、予測が 70% 以上なら橙、90% 以上なら赤になります。それ以外 (枠の開始直後など) は、使用率そのもので判定します (70% 未満は緑、90% 未満は橙、それ以上は赤)。

### 2. フライアウトで使用量を見る

トレイアイコンを左クリックすると、フライアウトが開きます。

<img src="docs/images/flyout-dark.png" alt="フライアウト" width="300">

- **Session (5h)**: 5 時間セッション枠の使用率とリセットまでの時間
- **Weekly (7d)**: 7 日間の週次枠の使用率とリセットまでの時間
- **右下の「Updated …」**: 最後に更新した時刻。マウスを乗せると正確な時刻を表示します
- **⟳**: 今すぐ更新
- **⚙**: 設定画面を開く
- **— / ✕**: フライアウトを閉じる (どちらもトレイに戻るだけで、アプリは終了しません)

ヘッダー部分をドラッグすると、フライアウトを移動できます。認証情報や通信に問題があるときは、使用率の上に警告が表示されます。

### 3. トレイアイコンの右クリックメニュー

| 項目 | 動作 |
|---|---|
| Refresh | 今すぐ更新 |
| Exit | アプリを終了 |

### 4. 設定を変える

フライアウトの ⚙ から設定画面を開きます。

<p>
  <img src="docs/images/settings-dark.png" alt="設定画面 (ダーク)" width="400">
  &nbsp;
  <img src="docs/images/settings-light.png" alt="設定画面 (ライト)" width="400">
</p>

| 項目 | 内容 |
|---|---|
| Claude Code CLI | 認証情報が見つかったかどうかを表示します。**Test connection** で実際に使用量を取得できるか確認できます |
| Refresh interval | 自動更新の間隔 (10〜3600 秒、既定は 60 秒)。数字のみ入力でき、− / ＋ボタン、↑ / ↓ キー、マウスホイールで 5 秒ずつ増減します |
| Appearance | System (Windows の設定に追従) / Light / Dark。選ぶとすぐプレビューされ、Cancel で元に戻ります |
| Enable threshold notifications | 使用率の通知を出すかどうか |
| Launch at Windows login | Windows へのサインイン時に自動起動するかどうか |
| Show window on startup | 起動時にフライアウトを開くかどうか |

**Save** で保存し、すぐに反映されます。

## 通知

**Enable threshold notifications** が有効なとき、次のタイミングで Windows の通知を出します。

- セッション枠の使用率が **75% / 90% / 95%** に達したとき (それぞれ 1 回だけ)
- セッション枠がリセットされたとき (使用率が 5% 超から 5% 未満に下がったとき)。リセット後は、各しきい値の通知がまた出るようになります

## 仕組み

1. **認証情報を探す**: Claude Code CLI 自身の認証情報ファイルを読み取ります (書き込みはしません)。
   - Windows: `%USERPROFILE%\.claude\.credentials.json` (環境変数 `CLAUDE_CONFIG_DIR` があればその下)
   - WSL: インストール済みの各ディストリビューションの `~/.claude/.credentials.json` を `wsl.exe` 経由で読み取り
   - Windows 側を優先し、見つからなければ WSL のディストリビューションを順に探します
2. **使用量を取得する**: Anthropic の API (`https://api.anthropic.com/api/oauth/usage`) から取得します。取得できない場合やリセット時刻が欠けている場合は、Messages API に最小限のリクエスト (1 トークン) を送り、レスポンスのレート制限ヘッダーから読み取ります。
3. **トークンを更新する**: トークンが期限切れ、または拒否された場合は、その環境の Claude Code CLI を `claude -p .` で起動してトークンを更新させ、読み直します。このアプリ自身が OAuth の更新処理を行うことはありません。

> **注意**: 手順 2 のフォールバックと手順 3 のトークン更新は、どちらも実際に Claude へリクエストを送るため、ごくわずかに使用量を消費します。

## 設定ファイル

設定は `%APPDATA%\ClaudeUsageTracker\settings.json` に保存されます。

| キー | 内容 | 既定値 |
|---|---|---|
| `RefreshIntervalSeconds` | 自動更新の間隔 (秒)。10〜3600 の範囲に丸められます | `60` |
| `NotificationsEnabled` | 使用率の通知 | `true` |
| `ShowFlyoutOnStartup` | 起動時にフライアウトを開く | `true` |
| `Theme` | `System` / `Light` / `Dark` | `System` |
| `NotifiedThresholdKeys` | 通知済みのしきい値 (同じ通知の重複防止用。通常は編集不要) | `[]` |

自動起動の設定だけは、このファイルではなくレジストリ (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) に保存されます。

## プロジェクト構成

```
src/
  ClaudeUsageTracker.App/        WPF アプリ本体 (画面、ViewModel、テーマ、トレイ、全体の組み立て)
  ClaudeUsageTracker.Core/       Windows に依存しないロジック (API クライアント、レスポンス解析、状態判定)
  ClaudeUsageTracker.Platform/   Windows 固有の処理 (Claude CLI / WSL の呼び出し、通知、自動起動、アイコン描画)
tests/
  ClaudeUsageTracker.Tests/      Core のユニットテスト (xUnit)
```

依存の向きは App → Platform → Core の一方向です。Core は `net10.0` を対象にしているため、Windows 固有の API が混入するとビルドエラーになります。

## テスト

```powershell
dotnet test
```
