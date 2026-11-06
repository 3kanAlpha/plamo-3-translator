using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace PlamoTranslator;

internal sealed class OllamaTranslator(HttpClient http)
{
    internal const int MaxCharacters = 4096;
    internal const string Model = "hf.co/pfnet/plamo-3-translate-2b-GGUF:Q4_K_M";
    internal const string PullCommand = "ollama pull " + Model;
    internal double? TokensPerSecond { get; private set; }

    // PFN公式の対応言語。中国語は簡体字・繁体字を別のコードで指定する。
    internal static IReadOnlyDictionary<string, string> Languages { get; } = new Dictionary<string, string>
    {
        ["ja"] = "日本語",
        ["en"] = "英語",
        ["zh-hans"] = "中国語（簡体字）",
        ["zh-hant"] = "中国語（繁体字）",
        ["ko"] = "韓国語",
        ["ar"] = "アラビア語",
        ["az"] = "アゼルバイジャン語",
        ["be"] = "ベラルーシ語",
        ["bg"] = "ブルガリア語",
        ["bn"] = "ベンガル語",
        ["ca"] = "カタルーニャ語",
        ["ceb"] = "セブアノ語",
        ["de"] = "ドイツ語",
        ["es"] = "スペイン語",
        ["et"] = "エストニア語",
        ["eu"] = "バスク語",
        ["fa"] = "ペルシャ語",
        ["fr"] = "フランス語",
        ["gu"] = "グジャラート語",
        ["he"] = "ヘブライ語",
        ["hi"] = "ヒンディー語",
        ["hr"] = "クロアチア語",
        ["hu"] = "ハンガリー語",
        ["hy"] = "アルメニア語",
        ["id"] = "インドネシア語",
        ["it"] = "イタリア語",
        ["jv"] = "ジャワ語",
        ["ka"] = "ジョージア語",
        ["kk"] = "カザフ語",
        ["km"] = "クメール語",
        ["lt"] = "リトアニア語",
        ["lv"] = "ラトビア語",
        ["ml"] = "マラヤーラム語",
        ["mr"] = "マラーティー語",
        ["ms"] = "マレー語",
        ["my"] = "ミャンマー語",
        ["ne"] = "ネパール語",
        ["nl"] = "オランダ語",
        ["pa"] = "パンジャーブ語",
        ["pl"] = "ポーランド語",
        ["ru"] = "ロシア語",
        ["si"] = "シンハラ語",
        ["sk"] = "スロバキア語",
        ["sl"] = "スロベニア語",
        ["sr"] = "セルビア語",
        ["sw"] = "スワヒリ語",
        ["ta"] = "タミル語",
        ["te"] = "テルグ語",
        ["th"] = "タイ語",
        ["tl"] = "タガログ語",
        ["ur"] = "ウルドゥー語",
        ["uz"] = "ウズベク語",
        ["vi"] = "ベトナム語"
    };

    internal static int CountCharacters(string source) =>
        source.ReplaceLineEndings("\n").EnumerateRunes().Count();

    internal static string? GetValidationError(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return "翻訳する文章を入力してください。";
        if (CountCharacters(source) > MaxCharacters)
            return $"原文は{MaxCharacters:N0}文字以内にしてください。入力内容は切り捨てていません。";
        if (source.Contains("<|plamo:", StringComparison.Ordinal))
            return "PLaMoの制御トークン（<|plamo:…）を含む文章は翻訳できません。";
        return null;
    }

    internal async Task CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync("api/tags", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Ollamaからモデル一覧を取得できませんでした。");
        if (!models.EnumerateArray().Any(model =>
                model.ValueKind == JsonValueKind.Object &&
                model.TryGetProperty("name", out var name) &&
                name.ValueKind == JsonValueKind.String && name.GetString() == Model))
            throw new InvalidOperationException("モデルが未登録です。PowerShellで次を実行してください。\n" + PullCommand);
    }

    internal async IAsyncEnumerable<string> TranslateAsync(
        string source, string sourceLanguage, string targetLanguage, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        TokensPerSecond = null;
        if (GetValidationError(source) is { } error)
            throw new ArgumentException(error, nameof(source));
        if (!Languages.ContainsKey(sourceLanguage) || !Languages.ContainsKey(targetLanguage))
            throw new ArgumentException("対応する入力言語と翻訳先の言語を選択してください。");

        var prompt = "<|plamo:tag|>dataset<|plamo:msg|>translation" +
            "<|plamo:tag|>input<|plamo:key|>lang<|plamo:val|>" + sourceLanguage +
            "<|plamo:msg|>" + source.ReplaceLineEndings("\n") +
            "<|plamo:tag|>output<|plamo:key|>lang<|plamo:val|>" + targetLanguage +
            (targetLanguage == "ja" ? "<|plamo:key|>form<|plamo:val|>polite" : "") + "<|plamo:msg|>";

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/generate")
        {
            Content = JsonContent.Create(new
            {
                model = Model,
                prompt,
                raw = true,
                stream = true,
                keep_alive = "5m",
                options = new
                {
                    temperature = 0,
                    // 4,096 Unicode文字のUTF-8バイト数と出力8,192トークンを収める。
                    num_ctx = 32768,
                    num_predict = 8192,
                    stop = new[] { "<|plamo:tag|>", "<|plamo:bos|>" }
                }
            })
        };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
        var output = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using var document = JsonDocument.Parse(line);
            var chunk = document.RootElement;
            if (chunk.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Ollamaから不正な応答が返りました。");
            if (chunk.TryGetProperty("error", out var serverError))
                throw new InvalidOperationException("Ollama: " + serverError.ToString());
            if (!chunk.TryGetProperty("done", out var done) ||
                done.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Ollamaの応答に完了状態がありません。");
            if (chunk.TryGetProperty("response", out var text))
            {
                if (text.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("Ollamaから不正な訳文が返りました。");
                if (text.GetString() is { Length: > 0 } part)
                {
                    output.Append(part);
                    yield return part;
                }
            }
            if (done.GetBoolean())
            {
                if (!chunk.TryGetProperty("done_reason", out var reason) || reason.GetString() != "stop")
                    throw new InvalidDataException("翻訳が途中で打ち切られました。原文を短くして再試行してください。");
                if (string.IsNullOrWhiteSpace(output.ToString()) || output.ToString().Contains("<|plamo:", StringComparison.Ordinal))
                    throw new InvalidDataException("正常な訳文を取得できませんでした。再試行してください。");
                if (chunk.TryGetProperty("eval_count", out var count) && count.ValueKind == JsonValueKind.Number &&
                    count.TryGetInt64(out var tokens) && tokens > 0 &&
                    chunk.TryGetProperty("eval_duration", out var duration) && duration.ValueKind == JsonValueKind.Number &&
                    duration.TryGetInt64(out var nanoseconds) && nanoseconds > 0)
                    TokensPerSecond = tokens / (nanoseconds / 1_000_000_000d);
                yield break;
            }
        }
        throw new EndOfStreamException("翻訳の途中で通信が切れました。再試行してください。");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        var message = response.ReasonPhrase;
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("error", out var error))
                message = error.ToString();
        }
        catch (JsonException) { }
        throw new HttpRequestException($"Ollama HTTP {(int)response.StatusCode}: {message}", null, response.StatusCode);
    }
}
