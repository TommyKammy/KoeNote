# KoeNote

KoeNote は、日本語の音声ファイルをローカル環境で文字起こしし、推敲レビュー、手修正、話者名の整理、エクスポートまで行うための Windows デスクトップアプリです。

音声や文字起こしデータをクラウドへ送らず、手元のPC内で処理することを前提に開発しています。

## いま使えること

- 音声ファイルをジョブとして登録
- ローカルASRによる文字起こし
- LLMによる修正候補のレビュー
- 修正候補の採用 / 却下 / 手修正
- セグメント本文の直接編集
- 話者IDの表示名変更
- 直前操作のUndo
- 過去の修正をもとにした correction memory
- 評価ベンチの実行
- TXT / Markdown / JSON / SRT / VTT へのエクスポート

## 開発状況

KoeNote は現在、Phase 10「パッケージングと初回起動」の準備段階です。

アプリ本体の主要ワークフローは実装済みですが、初めて利用する方向けのインストーラ、モデル配置ガイド、初回起動チェックはこれから整備します。そのため、現時点では開発版として起動する想定です。

## 必要な環境

- Windows 11
- .NET 11 preview SDK
- WPF が動作するWindows環境
- ffmpeg
- NVIDIA GPU 環境推奨
- `nvidia-smi`

ローカルASR/レビューを実際に動かす場合は、以下も必要です。

- `tools/crispasr.exe`
- `tools/llama-completion.exe`
- `models/asr/vibevoice-asr-q4_k.gguf`
- `models/review/llm-jp-4-8B-thinking-Q4_K_M.gguf`

ツールやモデルがない場合でも、アプリのUIや一部の保存/編集/エクスポート機能は開発確認できます。

## 開発版を起動する

リポジトリを取得したあと、ルートディレクトリで実行します。

```powershell
dotnet run --project src\KoeNote.App\KoeNote.App.csproj
```

テストを実行する場合:

```powershell
dotnet test
```

評価ベンチを実行する場合:

```powershell
dotnet run --project src\KoeNote.EvalBench\KoeNote.EvalBench.csproj
```

## 基本的な使い方

1. KoeNote を起動します。
2. 音声ファイルを追加します。
3. ジョブを選択して実行します。
4. 文字起こし結果を確認します。
5. 右側の推敲レビューで、候補を採用 / 却下 / 手修正します。
6. 必要に応じてセグメント本文や話者名を編集します。
7. Export から TXT / Markdown / JSON / SRT / VTT 形式で出力します。

未レビューの候補が残っている場合でもエクスポートはできますが、アプリ側で警告が表示されます。

## データの保存場所

既定では、ジョブやデータベースはユーザーのAppData配下に保存されます。

```text
%APPDATA%\KoeNote
```

ログはLocalAppData配下に保存されます。

```text
%LOCALAPPDATA%\KoeNote\logs
```

## エクスポート形式

現在のエクスポートは以下に対応しています。

- `.txt`
- `.md`
- `.json`
- `.srt`
- `.vtt`

エクスポート時の本文は、次の優先順で使われます。

```text
final_text → normalized_text → raw_text
```

つまり、手修正や採用済みの最終本文があればそれを使い、なければ正規化済みテキスト、最後にASRの生テキストを使います。

## フェーズ別ドキュメント

- [Phase 0 preflight](docs/phase0/README.md)
- [Phase 1 Windows runtime foundation](docs/phase1/README.md)
- [Phase 2 ASR worker](docs/phase2/README.md)
- [Phase 3 review worker](docs/phase3/README.md)
- [Phase 4 desktop UI](docs/phase4/README.md)
- [Phase 5 safe review decisions](docs/phase5/README.md)
- [Phase 6 editing, speaker aliases, and undo](docs/phase6/README.md)
- [Phase 7 correction memory](docs/phase7/README.md)
- [Phase 8 evaluation bench](docs/phase8/README.md)
- [Phase 9 export workflow](docs/phase9/README.md)
- [Phase 10 packaging and first run](docs/phase10/README.md)
- [Phase 11 beta operation](docs/phase11/README.md)

## 注意

KoeNote は開発中のアプリです。外部ベータ向けの配布形態、初回起動チェック、ライセンス表記、モデルパックの整理はPhase 10で整備予定です。
