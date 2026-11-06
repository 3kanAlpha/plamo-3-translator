using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PlamoTranslator;

internal sealed class MainForm : Form
{
    private readonly RichTextBox source = new()
    {
        Name = "sourceInput", AccessibleName = "原文", DetectUrls = false,
        ScrollBars = RichTextBoxScrollBars.Vertical, Dock = DockStyle.Fill, MaxLength = int.MaxValue
    };
    private readonly RichTextBox output = new()
    {
        Name = "translationOutput", AccessibleName = "訳文", DetectUrls = false, ReadOnly = true,
        ScrollBars = RichTextBoxScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = SystemColors.Window,
        MaxLength = int.MaxValue
    };
    private readonly ComboBox sourceLanguage = new()
    {
        Name = "sourceLanguage", AccessibleName = "入力言語", DisplayMember = "Value",
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 180
    };
    private readonly ComboBox targetLanguage = new()
    {
        Name = "targetLanguage", AccessibleName = "翻訳先", DisplayMember = "Value",
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 180
    };
    private readonly Button swap = new()
    {
        Name = "swapLanguagesButton", Text = "⇄", AccessibleName = "入力言語と翻訳先を入れ替える",
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 0, 8, 0)
    };
    private readonly Button translate = new() { Name = "translateButton", Text = "翻訳(&T)", AutoSize = true };
    private readonly Button cancel = new() { Name = "cancelButton", Text = "キャンセル(&C)", AutoSize = true, Enabled = false };
    private readonly Button copy = new() { Name = "copyButton", Text = "結果をコピー(&Y)", AutoSize = true, Enabled = false };
    private readonly Label count = new() { Name = "characterCount", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
    private readonly Label outputLabel = new() { Text = "訳文(&O)", AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
    private readonly Label status = new()
    {
        Name = "statusMessage", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly HttpClient http = new(new SocketsHttpHandler
    {
        UseProxy = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(5)
    }) { BaseAddress = new Uri("http://127.0.0.1:11434/"), Timeout = Timeout.InfiniteTimeSpan };
    private readonly OllamaTranslator translator;
    private CancellationTokenSource? operation;
    private bool closeRequested;

    internal MainForm()
    {
        translator = new OllamaTranslator(http);
        Text = "PLaMo Translate";
        using var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("PlamoTranslator.AppIcon.ico")!;
        Icon = new Icon(iconStream);
        Name = "mainForm";
        Font = new Font("Yu Gothic UI", 10);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1000, 680);
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;

        foreach (var language in OllamaTranslator.Languages)
        {
            sourceLanguage.Items.Add(language);
            targetLanguage.Items.Add(language);
        }
        sourceLanguage.SelectedIndex = 1;
        targetLanguage.SelectedIndex = 0;
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        toolbar.Controls.AddRange([
            new Label { Text = "入力言語(&I)", AutoSize = true, Margin = new Padding(0, 6, 8, 0) },
            sourceLanguage, swap,
            new Label { Text = "翻訳先(&D)", AutoSize = true, Margin = new Padding(8, 6, 8, 0) },
            targetLanguage, translate, cancel, copy
        ]);

        var editors = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty };
        editors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        editors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        editors.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editors.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editors.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editors.Controls.Add(new Label { Text = "原文(&S)", TabIndex = 0, AutoSize = true, Margin = new Padding(0, 8, 0, 8) }, 0, 0);
        source.TabIndex = 1;
        outputLabel.TabIndex = 2;
        output.TabIndex = 3;
        editors.Controls.Add(outputLabel, 1, 0);
        source.Margin = new Padding(0, 0, 12, 0);
        output.Margin = Padding.Empty;
        editors.Controls.Add(source, 0, 1);
        editors.Controls.Add(output, 1, 1);
        editors.Controls.Add(count, 0, 2);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(editors, 0, 1);
        layout.Controls.Add(status, 0, 2);
        Controls.Add(layout);

        source.TextChanged += (_, _) => UpdateControls();
        swap.Click += (_, _) => (sourceLanguage.SelectedIndex, targetLanguage.SelectedIndex) =
            (targetLanguage.SelectedIndex, sourceLanguage.SelectedIndex);
        translate.Click += async (_, _) => await TranslateAsync();
        cancel.Click += (_, _) => operation?.Cancel();
        copy.Click += (_, _) => CopyOutput();
        Shown += async (_, _) => await CheckConnectionAsync();
        FormClosing += (_, e) =>
        {
            if (operation is not null)
            {
                e.Cancel = true;
                closeRequested = true;
                operation.Cancel();
            }
        };
        FormClosed += (_, _) => http.Dispose();
        UpdateControls();
    }

    private async Task CheckConnectionAsync()
    {
        operation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        UpdateControls();
        SetStatus("Ollamaの接続を確認しています…");
        try
        {
            await translator.CheckAvailabilityAsync(operation.Token);
            SetStatus("準備完了。Ctrl+Enterで翻訳できます。");
        }
        catch (OperationCanceledException)
        {
            SetStatus("接続確認を終了しました。Ollamaを起動してから翻訳してください。", true);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException)
        {
            ShowError(ex);
        }
        finally { FinishOperation(); }
    }

    private async Task TranslateAsync()
    {
        if (operation is not null)
            return;
        if (OllamaTranslator.GetValidationError(source.Text) is { } error)
        {
            SetStatus(error, true);
            return;
        }
        operation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        UpdateControls();
        SetStatus("Ollamaの接続を確認しています…");
        var timer = Stopwatch.StartNew();
        var started = false;
        try
        {
            await translator.CheckAvailabilityAsync(operation.Token);
            output.Clear();
            outputLabel.Text = "訳文(&O)";
            started = true;
            SetStatus("翻訳中… 初回のモデル読み込みには時間がかかることがあります。");
            var sourceCode = ((KeyValuePair<string, string>)sourceLanguage.SelectedItem!).Key;
            var targetCode = ((KeyValuePair<string, string>)targetLanguage.SelectedItem!).Key;
            await foreach (var part in translator.TranslateAsync(source.Text, sourceCode, targetCode, operation.Token))
                output.AppendText(part);
            var speed = translator.TokensPerSecond is { } tokensPerSecond ? $"、{tokensPerSecond:F1} token/s" : "、生成速度は取得できませんでした";
            SetStatus($"翻訳完了（{timer.Elapsed.TotalSeconds:F1}秒{speed}）。結果をコピーできます。");
        }
        catch (OperationCanceledException)
        {
            if (started) MarkPartialOutput();
            SetStatus(timer.Elapsed >= TimeSpan.FromMinutes(3)
                ? "3分でタイムアウトしました。原文を短くして再試行してください。" : "翻訳をキャンセルしました。");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException)
        {
            if (started) MarkPartialOutput();
            ShowError(ex);
        }
        finally { FinishOperation(); }
    }

    private void MarkPartialOutput()
    {
        if (output.TextLength > 0)
            outputLabel.Text = "訳文（未完了）";
    }

    private void ShowError(Exception error) => SetStatus(error is HttpRequestException
        ? "Ollamaへの接続またはリクエストに失敗しました。Ollamaの起動状態を確認して再試行してください。\n" + error.Message
        : error.Message, true);

    private void FinishOperation()
    {
        operation?.Dispose();
        operation = null;
        UpdateControls();
        if (closeRequested)
            Close();
    }

    private void UpdateControls()
    {
        var characters = OllamaTranslator.CountCharacters(source.Text);
        var error = OllamaTranslator.GetValidationError(source.Text);
        count.Text = $"{characters:N0} / {OllamaTranslator.MaxCharacters:N0}文字" +
            (characters > OllamaTranslator.MaxCharacters ? "（上限超過）" : "") +
            (source.Text.Contains("<|plamo:", StringComparison.Ordinal) ? "（制御トークンは使用できません）" : "");
        count.ForeColor = characters > OllamaTranslator.MaxCharacters ? Color.Firebrick : SystemColors.ControlText;
        translate.Enabled = operation is null && error is null;
        cancel.Enabled = operation is not null;
        copy.Enabled = operation is null && output.TextLength > 0;
        source.ReadOnly = operation is not null;
        sourceLanguage.Enabled = operation is null;
        targetLanguage.Enabled = operation is null;
        swap.Enabled = operation is null;
    }

    private void SetStatus(string message, bool error = false)
    {
        status.Text = message;
        status.ForeColor = error ? Color.Firebrick : SystemColors.ControlText;
    }

    private void CopyOutput()
    {
        if (output.TextLength == 0)
            return;
        try
        {
            Clipboard.SetText(output.Text);
            SetStatus("訳文をクリップボードにコピーしました。");
        }
        catch (ExternalException)
        {
            SetStatus("クリップボードを使用できません。少し待って再試行してください。", true);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Enter) && translate.Enabled)
        {
            translate.PerformClick();
            return true;
        }
        if (keyData == Keys.Escape && cancel.Enabled)
        {
            cancel.PerformClick();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
