namespace TokenChecker.App;

// Small modal for calibrating the Copilot AI-credit current-period baseline after
// a mid-month plan change. GitHub's billing API returns the whole calendar month,
// but its dashboard resets the AI-usage counter on a plan change; the user enters
// the dashboard's current value and we store the offset to subtract (see
// CopilotBaseline). Calibration is applied IMMEDIATELY via the host, independent
// of the settings form's OK/Cancel (Clone preserves it on OK).
internal sealed class CopilotBaselineDialog : Form
{
    private readonly TrayApplicationContext _host;
    private readonly Label _reference = new();
    private readonly Label _status = new();
    private readonly NumericUpDown _value = new();

    public CopilotBaselineDialog(TrayApplicationContext host)
    {
        _host = host;

        Text = Strings.T("Copilot 利用量の補正");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9F);
        ClientSize = new Size(380, 214);

        var info = new Label
        {
            Text = Strings.T("プランを月の途中で変更すると管理画面の利用量はリセットされますが、アプリの取得値は今月の累計のままです。管理画面の現在値を入力すると、それに合わせて表示します。"),
            Location = new Point(14, 12),
            Size = new Size(352, 56),
            AutoSize = false
        };

        _reference.Location = new Point(14, 74);
        _reference.Size = new Size(352, 20);
        _reference.AutoSize = false;

        var valueLabel = new Label { Text = Strings.T("管理画面の現在値"), AutoSize = true, Location = new Point(14, 104) };
        _value.Location = new Point(160, 100);
        _value.Size = new Size(140, 24);
        _value.Minimum = 0;
        _value.Maximum = 100_000_000;
        _value.Increment = 1;
        _value.ThousandsSeparator = true;

        _status.Location = new Point(14, 134);
        _status.Size = new Size(352, 20);
        _status.AutoSize = false;
        _status.ForeColor = SystemColors.GrayText;

        var applyBtn = new Button { Text = Strings.T("この値で補正"), Location = new Point(14, 168), Size = new Size(120, 30) };
        applyBtn.Click += (_, _) => Apply();

        var clearBtn = new Button { Text = Strings.T("補正を解除"), Location = new Point(142, 168), Size = new Size(110, 30) };
        clearBtn.Click += (_, _) => Clear();

        var closeBtn = new Button
        {
            Text = Strings.T("閉じる"),
            Location = new Point(290, 168),
            Size = new Size(76, 30),
            DialogResult = DialogResult.OK
        };

        Controls.Add(info);
        Controls.Add(_reference);
        Controls.Add(valueLabel);
        Controls.Add(_value);
        Controls.Add(_status);
        Controls.Add(applyBtn);
        Controls.Add(clearBtn);
        Controls.Add(closeBtn);

        AcceptButton = closeBtn;
        CancelButton = closeBtn;

        RefreshState();
    }

    private void Apply()
    {
        if (_host.CurrentCopilotRawUsed() is null)
        {
            MessageBox.Show(
                this,
                Strings.T("利用量をまだ取得できていません。少し待ってから再度お試しください。"),
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _host.CalibrateCopilotBaseline((long)_value.Value);
        RefreshState();
    }

    private void Clear()
    {
        _host.ClearCopilotBaseline();
        RefreshState();
    }

    private void RefreshState()
    {
        var raw = _host.CurrentCopilotRawUsed();
        _reference.Text = raw is long r
            ? Strings.Tf("アプリ取得値（今月累計）: {0}", r.ToString("N0"))
            : Strings.T("アプリ取得値（今月累計）: 取得待ち");

        var (baseline, month) = _host.CurrentCopilotBaseline();
        var active = baseline is not null
            && string.Equals(month, CopilotBaseline.MonthKey(DateTimeOffset.UtcNow), StringComparison.Ordinal);
        _status.Text = active
            ? Strings.Tf("現在: 変更前 {0} を差し引き中（{1}）", baseline!.Value.ToString("N0"), month!)
            : Strings.T("現在: 補正なし");

        // Pre-fill with the current displayed (current-period) value so leaving it
        // unchanged is a no-op; the user just corrects it to the dashboard figure.
        if (raw is long rr)
        {
            var effective = CopilotBaseline.EffectiveUsed(rr, baseline, month, DateTimeOffset.UtcNow);
            _value.Value = Math.Min(_value.Maximum, effective);
        }
    }
}
