# PLaMo Translate

PLaMo 3 Translate 2BをOllamaで動かす、Windows用のシンプルな翻訳アプリです。
C# / WinForms / .NET 10で実装しています。追加のNuGetパッケージは使用していません。

## 機能

- 英語→日本語、日本語→英語を選択。日本語は「です・ます」。
- 原文は1回2,048文字まで。超過しても入力を切り捨てず、翻訳を無効にします。
- 訳文を順次表示し、キャンセル後は途中までの訳文を残します。
- 訳文のコピー。翻訳履歴は保存しません。

文字数はUnicodeコードポイントで数えます。空白と改行も含み、CRLF改行は1文字、`😀`も1文字です。
結合文字や複数のコードポイントから成る絵文字は、それぞれの構成要素を数えます。
PLaMoの制御トークン（`<|plamo:…`）を含む原文は受け付けません。

## 起動

必要なもの:

- Windowsと[.NET 10 Desktop Runtime](https://dotnet.microsoft.com/ja-jp/download/dotnet/10.0)（開発する場合は.NET 10 SDK）。
- 起動済みの[Ollama](https://ollama.com/download/windows)。アプリからの自動起動は行いません。
- 次の公式モデル（初回のみ取得）。

```powershell
ollama pull hf.co/pfnet/plamo-3-translate-2b-GGUF:Q4_K_M
```

この作業環境ではモデル取得済みです。作成済みの `artifacts\PlamoTranslator\PlamoTranslator.exe` を起動してください。
exeと同じフォルダ内のdllや設定ファイルも必要なので、exeだけを別の場所へ移動しないでください。

原文を貼り付け、翻訳方向を選んで「翻訳」を押します。`Ctrl+Enter`で翻訳、`Esc`でキャンセルできます。
起動時と翻訳前にOllamaへの接続とモデルの登録を確認します。停止中・モデル未登録の場合は画面下部に案内を表示します。

接続先は `http://127.0.0.1:11434` 固定です。プロキシとHTTPリダイレクトを使用せず、このPCのOllamaへ送信します。
原文・訳文はウィンドウを閉じると失われるため、必要な結果は先にコピーしてください。

## ビルドと検証

リポジトリのルートで実行します。

```powershell
dotnet build -c Release
dotnet run --project src/PlamoTranslator -c Release
```

通信エラー、途中切断、文字数の境界、UTF-8の分割受信、キャンセルを確認する、外部パッケージ不要のチェック:

```powershell
dotnet run --project tests/PlamoTranslator.Tests -c Release
```

起動中のOllamaと取得済みモデルを使った英日・日英、上限2,048文字、キャンセル後の再翻訳も確認する場合:

```powershell
dotnet run --project tests/PlamoTranslator.Tests -c Release -- --live
```

起動用フォルダを作る場合:

```powershell
dotnet publish src/PlamoTranslator -c Release --self-contained false -o artifacts/PlamoTranslator
```

## 制限

初回のモデル読み込みには時間がかかる場合があります。リクエストの上限時間は3分です。
生成上限や通信切断で止まった訳文は「未完了」と表示します。原文は保持されるので、短くして再試行できます。
同じ文章の繰り返しなどで、モデルの生成が長引くことがあります。

ファイル翻訳、OCR、常駐機能、言語の自動判定、自動分割は含みません。
翻訳の正確性やコード・数値・書式の完全な保持は保証されないため、利用前に結果を確認してください。

## モデルと実機検証

- [PFN公式モデル・使用手順](https://huggingface.co/pfnet/plamo-3-translate-2b-GGUF/blob/main/README_ja.md)
- [Ollamaでの実機検証記録](docs/ollama-verification.md)

モデルのライセンスは配布元のPLaMo Community Licenseに従ってください。
