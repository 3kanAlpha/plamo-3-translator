using System.Net;
using System.Text;
using System.Text.Json;
using PlamoTranslator;

namespace PlamoTranslator.Tests;

internal static class Program
{
    private static int assertions;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            RunAsync(args.Contains("--live")).GetAwaiter().GetResult();
            CheckForm();
            CheckThemeSettings();
            Console.WriteLine($"PASS: {assertions} assertions" + (args.Contains("--live") ? " (including live Ollama)" : ""));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static async Task RunAsync(bool live)
    {
        Assert(OllamaTranslator.GetValidationError(new string('文', 4096)) is null, "4,096文字を受け付ける");
        Assert(OllamaTranslator.GetValidationError(new string('文', 4097)) is not null, "4,097文字を拒否する");
        Assert(OllamaTranslator.CountCharacters("a\r\n😀") == 3, "改行と絵文字を各1文字として数える");
        Assert(OllamaTranslator.GetValidationError(string.Concat(Enumerable.Repeat("😀", 4096))) is null, "絵文字でも上限は4,096文字");
        Assert(OllamaTranslator.Languages.Count == 53 && OllamaTranslator.Languages.ContainsKey("zh-hans") &&
            OllamaTranslator.Languages.ContainsKey("zh-hant") && !OllamaTranslator.Languages.ContainsKey("zh"), "中国語の字体を含む公式53言語を用意する");
        Assert(OllamaTranslator.GetValidationError(" \r\n\t") is not null, "空白だけの原文を拒否する");
        Assert(OllamaTranslator.GetValidationError("text <|plamo:tag|> output") is not null, "制御トークンを拒否する");

        foreach (var (sourceLanguage, targetLanguage) in new[] { ("en", "ja"), ("ja", "en"), ("fr", "de"), ("zh-hans", "zh-hant"), ("zh-hant", "ko") })
        {
            using var http = Client(async (request, token) =>
            {
                Assert(request.RequestUri!.AbsolutePath == "/api/generate", "generate APIを使用する");
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var root = body.RootElement;
                Assert(root.GetProperty("raw").GetBoolean() && root.GetProperty("stream").GetBoolean(), "rawのストリームで送信する");
                Assert(root.GetProperty("model").GetString() == OllamaTranslator.Model, "検証済みモデルを使用する");
                var prompt = root.GetProperty("prompt").GetString()!;
                Assert(prompt.Contains("<|plamo:msg|> first\nsecond "), "原文の空白を保持して改行を正規化する");
                Assert(prompt.Contains("<|plamo:tag|>input<|plamo:key|>lang<|plamo:val|>" + sourceLanguage + "<|plamo:msg|>"),
                    "入力言語を指定する");
                Assert(prompt.EndsWith("<|plamo:tag|>output<|plamo:key|>lang<|plamo:val|>" + targetLanguage +
                    (targetLanguage == "ja" ? "<|plamo:key|>form<|plamo:val|>polite" : "") + "<|plamo:msg|>"), "翻訳先と日本語だけの文体を指定する");
                var options = root.GetProperty("options");
                Assert(options.GetProperty("num_ctx").GetInt32() >= 4096 * 4 + options.GetProperty("num_predict").GetInt32() + 256 &&
                    options.GetProperty("num_ctx").GetInt32() <= 32768,
                    "入力の最大UTF-8バイト数と出力のためのコンテキストを確保する");
                return Response("""
                    {"response":"会議😀","done":false}

                    {"response":"です。","done":false}
                    {"response":"","done":true,"done_reason":"stop","eval_count":20,"eval_duration":100000000,"load_duration":4000000000,"prompt_eval_duration":3000000000}
                    """);
            });
            var translator = new OllamaTranslator(http);
            Assert(await CollectAsync(translator, " first\r\nsecond ", sourceLanguage, targetLanguage) == "会議😀です。",
                "分割されたUTF-8と複数チャンクを結合できる");
            Assert(translator.TokensPerSecond == 200, "生成時間をナノ秒から換算し、ロード・入力処理時間を除いてtoken/sを計算する");
            await ThrowsAsync<ArgumentException>(() => CollectAsync(translator, new string('文', 4097)));
            Assert(translator.TokensPerSecond is null, "次のリクエストでは前回の速度を持ち越さない");
        }

        foreach (var metrics in new[] { "", ",\"eval_count\":20,\"eval_duration\":0", ",\"eval_count\":-1,\"eval_duration\":10",
                     ",\"eval_count\":20,\"eval_duration\":-1", ",\"eval_count\":20,\"eval_duration\":\"invalid\"" })
        {
            using var http = Client((_, _) => Task.FromResult(Response("{\"response\":\"text\",\"done\":true,\"done_reason\":\"stop\"" + metrics + "}")));
            var translator = new OllamaTranslator(http);
            Assert(await CollectAsync(translator) == "text" && translator.TokensPerSecond is null, "速度情報が欠落・不正でも翻訳は完了し、速度は表示しない");
        }

        using (var http = Client((_, _) => Task.FromResult(Response(JsonSerializer.Serialize(new
        {
            models = new[] { new { name = OllamaTranslator.Model } }
        })))))
            await new OllamaTranslator(http).CheckAvailabilityAsync(CancellationToken.None);
        using (var http = Client((_, _) => Task.FromResult(Response("{\"models\":[]}"))))
            await ThrowsAsync<InvalidOperationException>(() => new OllamaTranslator(http).CheckAvailabilityAsync(CancellationToken.None));

        var failures = new[]
        {
            "{\"response\":\"partial\",\"done\":true,\"done_reason\":\"length\"}",
            "{\"response\":\"  \",\"done\":true,\"done_reason\":\"stop\"}",
            "{\"response\":\"<|pla\",\"done\":false}\n{\"response\":\"mo:tag|>\",\"done\":true,\"done_reason\":\"stop\"}",
            "{\"response\":\"text\"}",
            "{\"response\":123,\"done\":false}"
        };
        foreach (var failure in failures)
        {
            using var http = Client((_, _) => Task.FromResult(Response(failure)));
            await ThrowsAsync<InvalidDataException>(() => CollectAsync(new OllamaTranslator(http)));
        }
        using (var http = Client((_, _) => Task.FromResult(Response("{\"response\":\"partial\",\"done\":false}"))))
            await ThrowsAsync<EndOfStreamException>(() => CollectAsync(new OllamaTranslator(http)));
        using (var http = Client((_, _) => Task.FromResult(Response("{not-json}"))))
            await ThrowsAsync<JsonException>(() => CollectAsync(new OllamaTranslator(http)));
        using (var http = Client((_, _) => Task.FromResult(Response("{\"error\":\"out of memory\"}"))))
            await ThrowsAsync<InvalidOperationException>(() => CollectAsync(new OllamaTranslator(http)));
        using (var http = Client((_, _) => Task.FromResult(Response("{\"error\":\"model not found\"}", HttpStatusCode.NotFound))))
        {
            var error = await ThrowsAsync<HttpRequestException>(() => CollectAsync(new OllamaTranslator(http)));
            Assert(error.StatusCode == HttpStatusCode.NotFound && error.Message.Contains("model not found"), "HTTPエラーの詳細を保持する");
        }
        using (var http = Client((_, _) => throw new Exception("Validation must happen before sending HTTP")))
        {
            await ThrowsAsync<ArgumentException>(() => CollectAsync(new OllamaTranslator(http), new string('文', 4097)));
            await ThrowsAsync<ArgumentException>(() => CollectAsync(new OllamaTranslator(http), sourceLanguage: "xx"));
            await ThrowsAsync<ArgumentException>(() => CollectAsync(new OllamaTranslator(http), targetLanguage: "ja<|plamo:tag|>"));
        }

        var blockedStream = new BlockingStream();
        using (var http = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(blockedStream)
        })))
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            await ThrowsAsync<OperationCanceledException>(() => CollectAsync(new OllamaTranslator(http), token: cancellation.Token));
        Assert(blockedStream.Disposed, "読み取り待機中でもキャンセルして通信を解放する");

        if (live)
            await CheckLiveAsync();
    }

    private static async Task CheckLiveAsync()
    {
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/"), Timeout = Timeout.InfiniteTimeSpan
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var translator = new OllamaTranslator(http);
        await translator.CheckAvailabilityAsync(timeout.Token);
        var japanese = await CollectAsync(translator, "Save a backup before updating the application.", "en", "ja", timeout.Token);
        Assert(japanese.Contains("バックアップ"), "実モデルで英日翻訳できる");
        Assert(translator.TokensPerSecond > 0, "実モデルの生成速度を取得できる");
        Console.WriteLine($"Live en → ja: {translator.TokensPerSecond:F1} token/s");
        var english = await CollectAsync(translator, "アプリを更新する前にバックアップを保存してください。", "ja", "en", timeout.Token);
        Assert(english.Contains("backup", StringComparison.OrdinalIgnoreCase), "実モデルで日英翻訳できる");
        var german = await CollectAsync(translator, "Le fichier a été enregistré.", "fr", "de", timeout.Token);
        Assert(german.Contains("Datei", StringComparison.OrdinalIgnoreCase), "実モデルでフランス語からドイツ語へ翻訳できる");
        foreach (var intoJapanese in new[] { true, false })
        {
            var document = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures",
                intoJapanese ? "technical-en.txt" : "technical-ja.txt"), timeout.Token);
            document = document.ReplaceLineEndings("\n");
            const string suffix = "\n7319";
            Assert(document.Length <= 4096 - suffix.Length, "技術文書の試験データが上限内に収まる");
            var longSource = document.PadRight(4096 - suffix.Length) + suffix;
            var result = await CollectAsync(translator, longSource, intoJapanese ? "en" : "ja", intoJapanese ? "ja" : "en", timeout.Token);
            Assert(OllamaTranslator.CountCharacters(longSource) == 4096 && result.Contains("7319"), "上限ちょうどの原文を末尾まで翻訳できる");
        }
        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
        {
            await ThrowsAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in translator.TranslateAsync(new string('文', 1000), "ja", "en", cancellation.Token))
                    cancellation.Cancel();
            });
        }
        Assert(translator.TokensPerSecond is null, "キャンセルした翻訳の速度は表示しない");
        Assert(!string.IsNullOrWhiteSpace(await CollectAsync(translator, "The file was saved.", "en", "ja", timeout.Token)),
            "実サーバーでもキャンセル後に再翻訳できる");
    }

    private static void CheckForm()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var settingsPath = Path.Combine(Path.GetTempPath(), "PlamoTranslator-tests-" + Guid.NewGuid(), "theme.txt");
        try
        {
            using var form = new MainForm(new ThemeSettings(settingsPath));
            var source = (RichTextBox)form.Controls.Find("sourceInput", true).Single();
            _ = source.Handle;
            var button = (Button)form.Controls.Find("translateButton", true).Single();
            var count = (Label)form.Controls.Find("characterCount", true).Single();
            var sourceLanguage = (ComboBox)form.Controls.Find("sourceLanguage", true).Single();
            var targetLanguage = (ComboBox)form.Controls.Find("targetLanguage", true).Single();
            Assert(button.Region is { } buttonRegion && !buttonRegion.IsVisible(0, 0) && !buttonRegion.IsVisible(1, 1) &&
                buttonRegion.IsVisible(button.Width / 2, button.Height / 2), "ボタンの角はクリック領域から除き、中央は含める");
            Assert(sourceLanguage.Items.Count == 53 && targetLanguage.Items.Count == 53, "両方の選択欄に53言語を用意する");
            Assert(((KeyValuePair<string, string>)sourceLanguage.SelectedItem!).Key == "en" &&
                ((KeyValuePair<string, string>)targetLanguage.SelectedItem!).Key == "ja", "初期値は英語から日本語");
            sourceLanguage.SelectedItem = OllamaTranslator.Languages.Single(language => language.Key == "fr");
            targetLanguage.SelectedItem = OllamaTranslator.Languages.Single(language => language.Key == "de");
            Assert(((KeyValuePair<string, string>)sourceLanguage.SelectedItem!).Key == "fr" &&
                ((KeyValuePair<string, string>)targetLanguage.SelectedItem!).Key == "de", "入力言語と翻訳先を独立して選択できる");
            Assert(!button.Enabled, "空欄では翻訳ボタンが無効");
            source.Text = new string('文', 4096);
            Assert(button.Enabled && count.Text.Contains("4,096 / 4,096"), "上限ちょうどでは翻訳可能");
            source.Text += "文";
            Assert(!button.Enabled && source.Text.Length == 4097 && count.Text.Contains("上限超過"), "上限超過を表示し、原文を切り捨てない");
            source.Text = "正常な入力";
            Assert(button.Enabled, "原文を短くすると再び翻訳可能");
            source.Text = "first\nsecond";
            Assert(source.GetPositionFromCharIndex(6).Y > source.GetPositionFromCharIndex(0).Y, "LF改行の原文を複数行に表示する");
            var output = (RichTextBox)form.Controls.Find("translationOutput", true).Single();
            _ = output.Handle;
            output.AppendText("first\n");
            output.AppendText("second");
            Assert(output.GetPositionFromCharIndex(6).Y > output.GetPositionFromCharIndex(0).Y, "順次受信した訳文の改行を表示する");
            var theme = (ComboBox)form.Controls.Find("themeSelector", true).Single();
            Assert(theme.SelectedIndex == 0 && theme.Items.Count == 3, "テーマの初期値はシステムで3種類から選べる");
            source.Select(1, 3);
            var sourceText = source.Text;
            theme.SelectedIndex = 2;
            Assert(source.Text == sourceText && output.Text == "first\nsecond" && source.SelectionStart == 1 && source.SelectionLength == 3,
                "ダーク切替で入力・訳文・選択範囲を保持する");
            if (!SystemInformation.HighContrast)
            {
                Assert(source.BackColor.GetBrightness() < 0.3f && source.ForeColor.GetBrightness() > 0.8f,
                    "ダークでは入力欄の背景が暗く文字が明るい");
                source.Text = new string('文', 4097);
                var errorColor = count.ForeColor;
                theme.SelectedIndex = 1;
                Assert(count.ForeColor != errorColor && count.Text.Contains("上限超過"), "切替後も上限エラーを配色に合わせて保持する");
                Assert(source.BackColor == Color.White && output.BackColor == Color.White, "ライトでは両方の本文欄が白い");
            }
            theme.SelectedIndex = 0;
            Assert(Application.IsDarkModeEnabled == (Application.SystemColorMode == SystemColorMode.Dark), "システムの現在の配色を反映する");
            using var bitmap = new Bitmap(form.Width, form.Height);
            foreach (var index in new[] { 1, 2, 0 })
            {
                theme.SelectedIndex = index;
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                foreach (Control control in sourceLanguage.Parent!.Controls)
                    Assert(Math.Abs(control.Top + control.Height / 2.0 - sourceLanguage.Top - sourceLanguage.Height / 2.0) <= 1,
                        $"ツールバーのラベル・選択欄・ボタンを縦方向に中央揃えする: {control.Name} {control.Bounds}, {sourceLanguage.Bounds}");
                var outputTitle = output.Parent!.Parent!.Controls.OfType<Label>().Single(label => label.Text == "訳文(&O)");
                Assert(outputTitle.Left >= output.Parent.Left, $"訳文の見出しを訳文欄の上に配置する: {outputTitle.Bounds}, {output.Parent.Bounds}");
                Assert(((KeyValuePair<string, string>)sourceLanguage.SelectedItem!).Key == "fr" &&
                    ((KeyValuePair<string, string>)targetLanguage.SelectedItem!).Key == "de",
                    "各テーマで画面を再描画しても言語の選択を保持する");
            }
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(settingsPath)))
                Directory.Delete(Path.GetDirectoryName(settingsPath)!, true);
        }
    }

    private static void CheckThemeSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PlamoTranslator-settings-" + Guid.NewGuid());
        var path = Path.Combine(directory, "theme.txt");
        var settings = new ThemeSettings(path);
        Assert(settings.Load() == SystemColorMode.System, "設定がない場合はシステム");
        try
        {
            foreach (var mode in new[] { SystemColorMode.Classic, SystemColorMode.Dark, SystemColorMode.System })
                Assert(settings.Save(mode) && settings.Load() == mode, "テーマを保存して読み戻せる");
            File.WriteAllText(path, "invalid");
            Assert(settings.Load() == SystemColorMode.System, "壊れた設定ではシステムへ戻る");
            var inaccessible = new ThemeSettings(Path.Combine(path, "theme.txt"));
            Assert(!inaccessible.Save(SystemColorMode.Dark), "書き込めない設定で例外を画面へ漏らさない");
            foreach (var dark in new[] { false, true })
            {
                var colors = ThemeColors.Create(dark, true);
                Assert(colors.Surface == SystemColors.Window && colors.Accent == SystemColors.Highlight,
                    "コントラストテーマではライト・ダークの指定よりOS配色を優先する");
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task<string> CollectAsync(OllamaTranslator translator, string source = "test", string sourceLanguage = "en", string targetLanguage = "ja",
        CancellationToken token = default)
    {
        var result = new StringBuilder();
        await foreach (var part in translator.TranslateAsync(source, sourceLanguage, targetLanguage, token))
            result.Append(part);
        return result.ToString();
    }

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new FakeHandler(send)) { BaseAddress = new Uri("http://127.0.0.1:11434/") };

    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StreamContent(new FragmentedStream(Encoding.UTF8.GetBytes(body))) };

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }

    private static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T error) { assertions++; return error; }
        throw new Exception($"Expected {typeof(T).Name}");
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 7)], cancellationToken);
    }

    private sealed class BlockingStream : MemoryStream
    {
        internal bool Disposed { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
