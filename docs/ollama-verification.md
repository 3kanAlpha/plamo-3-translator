# OllamaでのPLaMo 3 Translate 2B実機検証

検証日: 2026-11-06（日本時間）

## 結果

このWindows環境で、PFN公式のPLaMo 3 Translate 2B GGUFをOllamaから実行できた。
英日・日英、数千文字の入力、ストリーミング、通信のキャンセル後の再翻訳を確認した。
WinFormsからローカルHTTP APIを呼び出す構成で進められる。

## 環境

| 項目 | 実測・確認値 |
| --- | --- |
| Ollama | 0.40.15 |
| GPU | NVIDIA GeForce RTX 3070、VRAM 8 GiB |
| メモリ | 32 GiB |
| .NET SDK | 10.0.303 |
| API | http://127.0.0.1:11434 |
| モデル | `hf.co/pfnet/plamo-3-translate-2b-GGUF:Q4_K_M` |
| モデルdigest | `ea42a391aa5e9dd9f0264d3c1f8bb7d7d32596a89dd60036ff06baa12cd58a59` |
| モデル保存先 | `E:\ML\.ollama\models` |
| 検証コンテキスト | 8,192トークン |
| GPU常駐量 | API報告値 1,972,759,429 bytes（約1.84 GiB） |

Ollamaのログでは25/25層がGPUへ配置され、Flash Attentionが有効だった。
GPU常駐量はモデル用の報告値であり、デスクトップ等を含むGPU全体の使用量ではない。
独立したllama.cppの導入やOllamaの設定変更は行っていない。

## 実測

| 試験 | 原文の文字数 | 所要時間 | 生成速度 | 結果 |
| --- | ---: | ---: | ---: | --- |
| 英日・初回の短文 | 48 | 69.023秒 | 0.7 token/s | 完了 |
| 日英・ロード後の短文 | 18 | 0.086秒 | 172.4 token/s | 完了 |
| 英日・README | 598 | 0.901秒 | 164.8 token/s | 完了 |
| 日英・技術文書 | 254 | 0.657秒 | 160.6 token/s | 完了 |
| 英日・長文ストリーミング | 2,939 | 5.204秒 | 153.1 token/s | 最初の出力まで0.388秒 |
| 日英・長文 | 2,849 | 7.352秒 | 157.8 token/s | 完了 |
| モデル解放後の再ロード＋英日短文 | 46 | 5.036秒 | 136.2 token/s | ロードに4.856秒 |

初回はロードに18.1秒、入力処理と生成にも追加の待ち時間があった。
再ロードでは同じ遅延は再現しなかった。CUDAの初期化等が候補だが、原因は確定していない。
表は各1回の結果であり、平均値や性能保証ではない。

短文の例:

- 原文: The meeting has been rescheduled to next Monday.
- 訳文: 会議は来週の月曜日に変更されました。
- 原文: 会議は来週の月曜日に変更されました。
- 訳文: The meeting has been moved to next Monday.

READMEのサンプルでは、見出し、段落、PowerShellコードブロック、URL、Windowsパス、30秒・3回という数値を目視確認した。
長文試験には番号付きの同内容の節を繰り返す合成文章を使用し、末尾の確認番号789123まで出力されることを検査した。
通常完了した全リクエストで、空出力・生成上限での打ち切り・PLaMo制御トークンの混入がないことを検査した。
これは翻訳精度の網羅的な評価ではなく、実行可否と基本動作の確認である。

ストリーミングは12チャンクを受信した時点（約0.154秒）でCancellationTokenをキャンセルし、HTTP応答を破棄した。
続けて送った短文は約2.068秒で完了した。サーバーが停止するまでの厳密な時間は測定していない。

## 再実行するための最小手順

モデル取得（今回実行済み）:

```powershell
& 'E:\Program Files\Ollama\ollama.exe' pull hf.co/pfnet/plamo-3-translate-2b-GGUF:Q4_K_M
```

PowerShellからの英日翻訳:

```powershell
$payload = @{
    model = 'hf.co/pfnet/plamo-3-translate-2b-GGUF:Q4_K_M'
    prompt = '<|plamo:tag|>dataset<|plamo:msg|>translation' +
        '<|plamo:tag|>input<|plamo:key|>lang<|plamo:val|>en<|plamo:msg|>' +
        'Save a backup before updating the application.' +
        '<|plamo:tag|>output<|plamo:key|>lang<|plamo:val|>ja' +
        '<|plamo:key|>form<|plamo:val|>polite<|plamo:msg|>'
    raw = $true
    stream = $false
    keep_alive = '5m'
    options = @{
        temperature = 0
        num_ctx = 8192
        num_predict = 2048
        stop = @('<|plamo:tag|>', '<|plamo:bos|>')
    }
}
$body = [Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Depth 5))
$result = Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/generate' `
    -Method Post -ContentType 'application/json; charset=utf-8' `
    -Body $body -TimeoutSec 180
$result.response
```

日英ではinputの言語をja、outputの言語をenにし、form指定を外す。
長文試験の生成上限は4,096トークン。入力と出力の合計が設定したコンテキスト内に収まる必要がある。
アプリでは打ち切りを成功として表示せず、初回の待機中もキャンセルできるようにする。
入力中のPLaMo制御トークンやコンテキスト超過への対策は、アプリ実装時に必要。

## 一次情報

- [PFN公式GGUF README](https://huggingface.co/pfnet/plamo-3-translate-2b-GGUF/blob/main/README_ja.md): Ollamaでの取得方法、PFML3、raw指定、推論設定。モデルの学習時上限は入出力合計32,768トークンだが、今回の実行設定は8,192。
- [Ollama 0.40.15の内部ランナーバージョン](https://github.com/ollama/ollama/blob/v0.40.15/LLAMA_CPP_VERSION)
- [内部ランナーのPLaMo 3実装](https://github.com/ggml-org/llama.cpp/blob/b12111/src/models/plamo3.cpp)

## アプリ実装後の追加検証

同日、入力上限2,048文字のWinFormsアプリを実装した。
入力をUnicodeコードポイントで数え、CRLFは1改行として扱う。
最大入力のUTF-8バイト数と出力枠を確保するため、アプリではコンテキストを16,384、生成上限を4,096トークンに設定した。
上記の8,192トークンでの初期測定とは設定が異なる。

外部テストパッケージを追加しないチェックで、次を確認した。

- 2,048文字の受け付けと2,049文字の拒否。超過した原文を切り捨てず、短くすると翻訳を再び有効にする。
- UTF-8の分割受信、英日・日英のプロンプト、敬体指定、制御トークンを含む入力の拒否。
- モデル未登録、HTTPエラー、JSONの不正、途中切断、生成上限、空出力を成功として扱わない。
- ストリームの読み取り待機中でもキャンセルし、通信を解放する。
- 実モデルで英日・日英、両方向の2,048文字の原文を末尾の確認番号まで翻訳する。
- 実モデルでキャンセル後に再翻訳する。

`dotnet run --project tests/PlamoTranslator.Tests -c Release -- --live` は48項目のチェックに成功した。
文字数上限の実翻訳には `tests/PlamoTranslator.Tests/Fixtures` の技術文書を使用し、空白で2,048文字に揃えて末尾に確認番号を付けた。
これとは別に、同じ短い段落を繰り返す合成入力では生成が4,096トークンまで続いたケースがあり、打ち切りとして検出された。
モデルがすべての入力を必ず翻訳し終えることを保証するものではない。

実際のGUIでは、英日・日英の翻訳、コピーした訳文の貼り付け、2,049文字での翻訳ボタン無効化を確認した。

## 4,096文字・多言語選択・生成速度への更新

入力上限を4,096文字に変更し、入力言語と翻訳先を別々の選択欄にした。
PFN公式の53言語を登録し、中国語は `zh-hans` と `zh-hant` を区別する。
日本語を翻訳先にした場合だけ敬体を指定する。

コンテキストは32,768、生成上限は8,192トークンに変更した。
最大入力のUTF-8バイト数と生成上限を合計してもコンテキストに収まることをチェックしている。
実機の `/api/ps` では `context_length=32768`、`size_vram=2023091077` を確認した。

生成速度は正常完了時の `eval_count / (eval_duration / 1,000,000,000)` で計算し、画面下部の完了メッセージに表示する。
ロードと入力処理にかかった時間は含まない。統計が欠落・不正な場合は速度取得不可と表示し、キャンセルや打ち切り時は速度を表示しない。

- 通常のチェック92項目、実モデルを含むチェック103項目が成功した。
- 入力4,096文字の受け付け、4,097文字の拒否、UTF-8の最大サイズ、両方向の4,096文字の実翻訳を確認した。
- 英日・日英に加えて、実モデルでフランス語→ドイツ語を確認した。
- 中国語の字体指定など5組の言語ペアのプロンプトと、不正な言語コードの拒否を確認した。
- ナノ秒からの速度計算、統計欠落・ゼロ・不正値、前回の速度を次のリクエストに持ち越さないことを確認した。
- Releaseビルドは警告・エラーともに0。起動用フォルダも更新した。

GUIでも言語選択を変更して、`Le fichier a été enregistré.` → `Die Datei wurde gespeichert.` の翻訳と、完了メッセージへのtoken/s表示を確認した。
英日では空行を含む原文・訳文の表示も確認した。
ここで表示されるtoken/sは各リクエストの生成速度であり、短文の結果を継続的な性能の保証には使用しない。

## 未検証

長時間連続利用、他のPC、全53言語の全組み合わせの翻訳精度、専門用語の網羅的な精度、最大コンテキスト付近の入力は未検証。
