using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace PlamoTranslator;

internal sealed class OllamaTranslator(HttpClient http)
{
    internal const int MaxCharacters = 2048;
    internal const string Model = "hf.co/pfnet/plamo-3-translate-2b-GGUF:Q4_K_M";
    internal const string PullCommand = "ollama pull " + Model;

    internal static int CountCharacters(string source) =>
        source.ReplaceLineEndings("\n").EnumerateRunes().Count();

    internal static string? GetValidationError(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return "翻訳する文章を入力してください。";
        if (CountCharacters(source) > MaxCharacters)
            return "原文は2,048文字以内にしてください。入力内容は切り捨てていません。";
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
        string source, bool intoJapanese, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (GetValidationError(source) is { } error)
            throw new ArgumentException(error, nameof(source));

        var prompt = "<|plamo:tag|>dataset<|plamo:msg|>translation" +
            "<|plamo:tag|>input<|plamo:key|>lang<|plamo:val|>" + (intoJapanese ? "en" : "ja") +
            "<|plamo:msg|>" + source.ReplaceLineEndings("\n") +
            "<|plamo:tag|>output<|plamo:key|>lang<|plamo:val|>" + (intoJapanese ? "ja" : "en") +
            (intoJapanese ? "<|plamo:key|>form<|plamo:val|>polite" : "") + "<|plamo:msg|>";

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
                    // 2,048 Unicode文字のUTF-8バイト数と出力4,096トークンを収める。
                    num_ctx = 16384,
                    num_predict = 4096,
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
