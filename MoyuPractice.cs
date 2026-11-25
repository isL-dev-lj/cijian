using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("词间")]
[assembly: System.Reflection.AssemblyProduct("词间")]
[assembly: System.Reflection.AssemblyVersion("1.0.4.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.4.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.0.4")]

internal static class Program
{
    internal const string AppName = "词间";
    internal static readonly Icon AppIcon = LoadIcon();

    private static Icon LoadIcon()
    {
        using (var stream = typeof(Program).Assembly.GetManifestResourceStream("Cijian.ico"))
            return new Icon(stream);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 4 && args[0] == "--install-update")
            return CijianUpdates.Apply(args[1], args[2], args[3]);
        if (args.Length == 2 && args[0] == "--test-update")
            return CijianUpdates.TestPackage(args[1]);
        if (args.Length == 2 && args[0] == "--cleanup-update")
            CijianUpdates.Cleanup(args[1]);
        if (args.Contains("--self-test"))
            return PracticeWindow.SelfTest(args.Length > 1 ? args[1] : null);
        if (args.Length == 2 && (args[0] == "--test-save-brush" || args[0] == "--test-resume-brush"))
            return PracticeWindow.TestBrushRestart(Path.GetFullPath(args[1]), args[0] == "--test-save-brush");
        bool firstInstance;
        using (var instance = new System.Threading.Mutex(true, "Local\\MoyuPractice", out firstInstance))
        {
            if (!firstInstance) return 0;
            using (var context = new PracticeContext())
                Application.Run(context);
        }
        return 0;
    }

}

internal sealed class PracticeContext : ApplicationContext
{
    private readonly PracticeWindow window = new PracticeWindow();
    private readonly NotifyIcon tray = new NotifyIcon();
    private ManagementWindow management;

    public PracticeContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开练习", null, (sender, args) => window.Open());
        menu.Items.Add("管理练习", null, (sender, args) => OpenManagement());
        menu.Items.Add("软件更新", null, (sender, args) => ShowUpdates());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (sender, args) => ExitThread());
        tray.ContextMenuStrip = menu;
        tray.Icon = Program.AppIcon;
        tray.Text = Program.AppName;
        tray.Visible = true;
        tray.DoubleClick += (sender, args) => OpenManagement();
        window.InitializeShortcut();
        OpenManagement();
    }

    private void OpenManagement()
    {
        if (window.Visible) window.ClosePopup();
        if (management == null || management.IsDisposed)
            management = new ManagementWindow(window, ShowUpdates);
        management.Show();
        if (management.WindowState == FormWindowState.Minimized)
            management.WindowState = FormWindowState.Normal;
        management.Activate();
    }

    private void ShowUpdates()
    {
        if (window.Visible) window.ClosePopup();
        if (CijianUpdates.ShowDialog()) ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tray.Visible = false;
            tray.ContextMenuStrip.Dispose();
            tray.Dispose();
            if (management != null) management.Dispose();
            window.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class ManagementWindow : Form
{
    private readonly PracticeWindow practice;
    private readonly RadioButton[] decks;
    private readonly RadioButton[] modes;
    private readonly CheckBox speech = new CheckBox();
    private readonly CheckBox sounds = new CheckBox();
    private readonly Label progress = new Label();
    private readonly Label modeHelp = new Label();
    private readonly Label shortcut = new Label();
    private bool syncing;
    private static readonly string[] ModeHelp = {
        "看着完整拼写输入，适合熟悉新词。",
        "补全隐藏的字母，按 Tab 可提示下一字符。",
        "根据释义回忆拼写，按 Tab 可提示下一字符。",
        "连续跟打，单独保存练习进度。"
    };

    public ManagementWindow(PracticeWindow practice, Action showUpdates = null)
    {
        this.practice = practice;
        Text = Program.AppName;
        Icon = Program.AppIcon;
        Font = new Font("Microsoft YaHei UI", 10);
        AutoScaleMode = AutoScaleMode.Font;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(500, 474);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.FromArgb(247, 248, 245);
        ForeColor = Color.FromArgb(32, 42, 38);

        var title = new Label { Text = Text, Bounds = new Rectangle(26, 21, 445, 42),
            Font = new Font(Font.FontFamily, 22, FontStyle.Bold) };
        Controls.Add(title);
        shortcut.Bounds = new Rectangle(28, 67, 445, 25);
        shortcut.ForeColor = Color.FromArgb(78, 91, 84);
        Controls.Add(shortcut);

        var options = new Panel { BackColor = Color.White, Bounds = new Rectangle(26, 106, 448, 184) };
        var audio = new Panel { BackColor = Color.White, Bounds = new Rectangle(26, 304, 448, 91) };
        Controls.AddRange(new Control[] { options, audio });
        AddLabel(options, "练习内容", 17, 16);
        AddLabel(options, "练习模式", 17, 99);
        decks = AddChoices(options, 15, new object[] { "六级词库", "六级句子库", "考研大纲词频" },
            index => practice.SelectDeck((PracticeWindow.Deck)index));
        modes = AddChoices(options, 98, PracticeWindow.ModeNames, practice.SelectMode);
        options.Controls.AddRange(new Control[] { progress, modeHelp });
        progress.Bounds = new Rectangle(112, 52, 315, 27);
        modeHelp.Bounds = new Rectangle(112, 136, 315, 30);
        progress.ForeColor = modeHelp.ForeColor = shortcut.ForeColor;
        AddLabel(audio, "声音", 17, 14);
        speech.Text = "自动朗读";
        sounds.Text = "练习提示音";
        speech.Bounds = new Rectangle(17, 42, 143, 29);
        sounds.Bounds = new Rectangle(176, 42, 165, 29);
        audio.Controls.AddRange(new Control[] { speech, sounds });

        var start = new Button { Text = "打开练习", Bounds = new Rectangle(328, 414, 146, 37),
            BackColor = Color.FromArgb(32, 54, 45), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        start.Click += (sender, args) => { Hide(); practice.Open(); };
        Controls.Add(start);
        AcceptButton = start;
        if (showUpdates != null)
        {
            var updates = new LinkLabel { Text = "软件更新  ·  " + CijianUpdates.DisplayVersion,
                Bounds = new Rectangle(28, 421, 230, 25) };
            updates.LinkClicked += (sender, args) => showUpdates();
            Controls.Add(updates);
        }

        speech.CheckedChanged += (sender, args) => { if (!syncing) practice.SetAutomaticSpeech(speech.Checked); };
        sounds.CheckedChanged += (sender, args) => { if (!syncing) practice.SetTypingSound(sounds.Checked); };
        Activated += (sender, args) => RefreshSettings();
        RefreshSettings();
    }

    private static void AddLabel(Control parent, string text, int x, int y)
    {
        parent.Controls.Add(new Label { Text = text, Bounds = new Rectangle(x, y, 95, 27) });
    }

    private RadioButton[] AddChoices(Control parent, int y, object[] choices, Action<int> onSelect)
    {
        var group = new Panel { Bounds = new Rectangle(112, y, 321, 33) };
        parent.Controls.Add(group);
        var buttons = new RadioButton[choices.Length];
        int width = group.Width / choices.Length;
        for (int i = 0; i < choices.Length; i++)
        {
            int index = i;
            var button = new RadioButton
            {
                Text = choices[i].ToString(), Appearance = Appearance.Button,
                TextAlign = ContentAlignment.MiddleCenter, FlatStyle = FlatStyle.Flat,
                Bounds = new Rectangle(i * width, 0, width - 5, 31)
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(217, 224, 219);
            button.FlatAppearance.CheckedBackColor = Color.FromArgb(218, 233, 224);
            button.CheckedChanged += (sender, args) =>
            {
                if (syncing || !button.Checked) return;
                onSelect(index);
                RefreshSummary();
            };
            group.Controls.Add(button);
            buttons[i] = button;
        }
        return buttons;
    }

    private void RefreshSettings()
    {
        syncing = true;
        decks[(int)practice.SelectedDeck].Checked = true;
        modes[practice.SelectedMode].Checked = true;
        speech.Checked = practice.AutomaticSpeech;
        sounds.Checked = practice.TypingSoundEnabled;
        syncing = false;
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        shortcut.Text = practice.PracticeShortcut;
        progress.Text = practice.DeckProgress;
        modeHelp.Text = ModeHelp[practice.SelectedMode];
    }

    internal bool VerifyControls()
    {
        decks[(int)PracticeWindow.Deck.Sentences].Checked = true;
        modes[1].Checked = true;
        speech.Checked = true;
        sounds.Checked = true;
        return !practice.Visible && practice.SelectedDeck == PracticeWindow.Deck.Sentences &&
            practice.SelectedMode == 1 && practice.AutomaticSpeech && practice.TypingSoundEnabled &&
            progress.Text.Contains("20");
    }
}

internal sealed class PracticeWindow : Form
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr handle, int id);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    private static readonly string[][] Words = {
        new[] { "account for", "/əˈkaʊnt fɔːr/", "解释；占（比例）" },
        new[] { "substantial", "/səbˈstænʃəl/", "大量的；重大的" },
        new[] { "consequence", "/ˈkɑːnsəkwens/", "后果；结果" },
        new[] { "take into account", "/teɪk ˈɪntu əˈkaʊnt/", "把……考虑在内" },
        new[] { "contribute", "/kənˈtrɪbjuːt/", "促成；贡献" },
        new[] { "in contrast", "/ɪn ˈkɑːntræst/", "相比之下" },
        new[] { "sustainable", "/səˈsteɪnəbəl/", "可持续的" },
        new[] { "evidence", "/ˈevɪdəns/", "证据；依据" },
        new[] { "assume", "/əˈsuːm/", "假定；承担" },
        new[] { "assess", "/əˈses/", "评估" },
        new[] { "allocate", "/ˈæləkeɪt/", "分配" },
        new[] { "emerge", "/ɪˈmɜːrdʒ/", "出现；显现" },
        new[] { "decline", "/dɪˈklaɪn/", "下降；婉拒" },
        new[] { "restrict", "/rɪˈstrɪkt/", "限制" },
        new[] { "imply", "/ɪmˈplaɪ/", "暗示；意味着" },
        new[] { "prevalent", "/ˈprevələnt/", "普遍存在的" },
        new[] { "ambiguous", "/æmˈbɪɡjuəs/", "含义不明确的" },
        new[] { "potential", "/pəˈtenʃəl/", "潜在的；潜力" },
        new[] { "reluctant", "/rɪˈlʌktənt/", "不情愿的" },
        new[] { "consistent", "/kənˈsɪstənt/", "一致的；持续的" },
        new[] { "incentive", "/ɪnˈsentɪv/", "激励；诱因" },
        new[] { "constraint", "/kənˈstreɪnt/", "限制；约束" },
        new[] { "perspective", "/pərˈspektɪv/", "视角；观点" },
        new[] { "feasible", "/ˈfiːzəbəl/", "可行的" },
        new[] { "enhance", "/ɪnˈhæns/", "提升；增强" },
        new[] { "prioritize", "/praɪˈɔːrətaɪz/", "优先处理" },
        new[] { "undermine", "/ˌʌndərˈmaɪn/", "削弱；损害" },
        new[] { "regulate", "/ˈreɡjəleɪt/", "监管；调节" },
        new[] { "intensify", "/ɪnˈtensɪfaɪ/", "加剧；加强" },
        new[] { "confront", "/kənˈfrʌnt/", "面对；直面" },
        new[] { "no longer", "/noʊ ˈlɔːŋɡər/", "不再" },
        new[] { "independent of", "/ˌɪndɪˈpendənt əv/", "独立于；不依赖" },
        new[] { "at odds with", "/ət ˈɑːdz wɪð/", "与……有分歧" },
        new[] { "give rise to", "/ɡɪv ˈraɪz tu/", "导致；引起" },
        new[] { "to some extent", "/tə səm ɪkˈstent/", "在某种程度上" },
        new[] { "be attributed to", "/bi əˈtrɪbjuːtɪd tu/", "被归因于" },
        new[] { "point out", "/pɔɪnt ˈaʊt/", "指出" },
        new[] { "rule out", "/ruːl ˈaʊt/", "排除" },
        new[] { "rely on", "/rɪˈlaɪ ɑːn/", "依赖；依靠" },
        new[] { "in the long run", "/ɪn ðə ˌlɔːŋ ˈrʌn/", "从长远来看" }
    };
    private static readonly string[][] Sentences = {
        new[] { "Small firms can no longer operate independently of major platforms", "", "小企业已无法脱离大型平台独立经营。" },
        new[] { "The report takes regional differences into account", "", "这份报告考虑了地区差异。" },
        new[] { "Online sales account for half of the total revenue", "", "线上销售占总收入的一半。" },
        new[] { "These findings do not rule out other explanations", "", "这些研究结果并未排除其他解释。" },
        new[] { "New rules may give rise to unexpected costs", "", "新规可能带来意想不到的成本。" },
        new[] { "The evidence is consistent with our initial assumption", "", "证据与我们最初的假设一致。" },
        new[] { "The two studies reach substantially different conclusions", "", "这两项研究得出了截然不同的结论。" },
        new[] { "The policy was designed to protect consumers and encourage competition", "", "这项政策旨在保护消费者并促进竞争。" },
        new[] { "A decline in demand does not necessarily imply a decline in quality", "", "需求下降并不一定意味着质量下降。" },
        new[] { "The change can be attributed in part to better public transport", "", "这一变化部分归因于公共交通的改善。" },
        new[] { "Students were reluctant to rely on a single source", "", "学生们不愿只依赖一个信息来源。" },
        new[] { "We need to assess whether the proposal is feasible", "", "我们需要评估这项提议是否可行。" },
        new[] { "Short-term savings may undermine progress in the long run", "", "短期节省的开支可能会妨碍长期进展。" },
        new[] { "Local governments allocate funds according to need", "", "地方政府按需求分配资金。" },
        new[] { "Greater flexibility can help employees manage competing demands", "", "更大的灵活性有助于员工应对相互冲突的要求。" },
        new[] { "Public attitudes may change as more evidence emerges", "", "随着更多证据出现，公众态度可能会改变。" },
        new[] { "Researchers point out that the sample was relatively small", "", "研究人员指出，这个样本相对较小。" },
        new[] { "The findings should be interpreted in the context of local conditions", "", "应结合当地情况解读这些研究结果。" },
        new[] { "In contrast the second approach requires less time and fewer resources", "", "相比之下，第二种方法所需的时间更短、资源更少。" },
        new[] { "The long-term consequences are still difficult to predict", "", "长期后果仍然难以预测。" }
    };
    private readonly string statePath;
    private readonly ComboBox mode = new ComboBox();
    private readonly Label status = new Label();
    private readonly TextBox input = new TextBox();
    private readonly CheckBox automatic = new CheckBox();
    private readonly Timer feedback = new Timer();
    private readonly Font spellingFont = new Font("Consolas", 15);
    private readonly Font queueFont = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
    private readonly Font smallFont = new Font("Microsoft YaHei UI", 9);
    private const int QueueLength = 5;
    private static readonly Color ContentSurface = Color.FromArgb(255, 255, 255);
    private static readonly Color ActiveSurface = Color.FromArgb(25, 25, 25);
    private enum Result { Pending, Incorrect, Correct }
    private Result result;
    private Point? dragStart;
    private Point dragWindowOrigin;
    private bool dragging;
    private bool wasMoved;
    private bool playOnClick;
    private Rectangle graduateStar;
    private Rectangle firstPronunciationLine;
    private Rectangle secondPronunciationLine;
    private Rectangle CloseButtonBounds { get { return new Rectangle(Width - 30, 8, 24, 30); } }
    private SpeechSynthesizer speech;
    private readonly System.Windows.Media.MediaPlayer onlineSpeech = new System.Windows.Media.MediaPlayer();
    private readonly Timer speechTimeout = new Timer { Interval = 4500 };
    private System.Net.WebClient speechRequest;
    private string speechFile;
    private int speechVersion;
    private SoundPlayer typingSound;
    private SoundPlayer correctSound;
    private SoundPlayer incorrectSound;
    public bool TypingSoundEnabled { get; private set; }
    private IntPtr previousWindow;
    private bool hotkeyRegistered;
    private bool usingAlternateHotkey;
    private bool updating;
    private int completed;
    private int brushed;
    private int sentencesCompleted;
    private int sentencesBrushed;
    private int graduateCompleted;
    private int graduateBrushed;
    private const int GraduateGroupSize = 50;
    private const int GraduateReviewSlots = 10;
    private readonly System.Collections.Generic.HashSet<int> graduateFavorites =
        new System.Collections.Generic.HashSet<int>();
    private int[] graduateGroup;
    private int graduateGroupStart;
    private int graduateNext;
    private int GraduatePosition { get { return graduateCompleted % GraduateGroupSize; } }
    public enum Deck { Words, Sentences, Graduate }
    public Deck SelectedDeck { get; private set; }
    public static readonly object[] ModeNames = { "跟打", "挖空", "记忆", "刷词" };
    public int SelectedMode { get { return mode.SelectedIndex; } }
    public bool AutomaticSpeech { get { return automatic.Checked; } }
    public string PracticeShortcut
    {
        get
        {
            if (!hotkeyRegistered) return "快捷键被占用，可从托盘打开练习";
            return (usingAlternateHotkey ? "Ctrl+Shift+F10" : "Ctrl+Shift+O") + " 呼出浮窗 · Esc 收起";
        }
    }
    public string DeckProgress
    {
        get
        {
            if (UsingGraduate)
                return GraduateProgress + "   ·   收藏 " + graduateFavorites.Count.ToString("N0") + " 个";
            return CurrentDeck.Length.ToString("N0") + (UsingSentences ? " 句" : " 个词条") +
                "   ·   已完成 " + CompletedCount.ToString("N0") + " 次";
        }
    }
    private bool UsingSentences { get { return SelectedDeck == Deck.Sentences; } }
    private bool UsingGraduate { get { return SelectedDeck == Deck.Graduate; } }
    private static readonly Lazy<string[][]> GraduateWords = new Lazy<string[][]>(LoadGraduateWords);
    private string[][] CurrentDeck { get { return UsingGraduate ? GraduateWords.Value : (UsingSentences ? Sentences : Words); } }
    private int CompletedCount { get { return UsingGraduate ? graduateCompleted : (UsingSentences ? sentencesCompleted : completed); } }
    private bool IsBrushing { get { return mode.SelectedIndex == 3; } }
    private string[] CurrentWord { get { return WordAt(0); } }
    private string[] WordAt(int offset)
    {
        var deck = CurrentDeck;
        if (UsingGraduate) return deck[GraduateIndex(offset)];
        int count = CurrentCount;
        return deck[(count % deck.Length + offset) % deck.Length];
    }

    private string GraduateProgress
    {
        get { return "第 " + (graduateCompleted / GraduateGroupSize + 1) + " 组 · " + GraduatePosition + "/50"; }
    }

    private int GraduateIndex(int offset)
    {
        if (IsBrushing) return (graduateBrushed % GraduateWords.Value.Length + offset) % GraduateWords.Value.Length;
        EnsureGraduateGroup();
        return graduateGroup[GraduatePosition + offset];
    }

    private void EnsureGraduateGroup()
    {
        if (graduateGroup != null) return;
        int length = GraduateWords.Value.Length;
        int[] upcoming = Enumerable.Range(0, GraduateGroupSize)
            .Select(offset => (graduateGroupStart % length + offset) % length).ToArray();
        var seen = graduateFavorites.Where(index => index < graduateGroupStart && !upcoming.Contains(index))
            .OrderBy(index => index).ToArray();
        int reviewStart = seen.Length == 0 ? 0 :
            (int)((long)(graduateCompleted / GraduateGroupSize) * GraduateReviewSlots % seen.Length);
        var review = seen.Skip(reviewStart).Concat(seen.Take(reviewStart)).Take(GraduateReviewSlots).ToArray();
        var fresh = upcoming.Take(GraduateGroupSize - review.Length).ToArray();
        graduateGroup = review.Concat(fresh.OrderByDescending(index => graduateFavorites.Contains(index))).ToArray();
        graduateNext = graduateGroupStart + fresh.Length;
    }

    private void ToggleGraduateFavorite()
    {
        int index = GraduateIndex(0);
        if (!graduateFavorites.Add(index)) graduateFavorites.Remove(index);
        SaveProgress();
        Invalidate();
    }
    private int CurrentCount
    {
        get
        {
            if (UsingGraduate) return IsBrushing ? graduateBrushed : graduateCompleted;
            if (UsingSentences) return IsBrushing ? sentencesBrushed : sentencesCompleted;
            return IsBrushing ? brushed : completed;
        }
    }

    private static string[][] LoadGraduateWords()
    {
        using (var reader = new StreamReader(typeof(PracticeWindow).Assembly.GetManifestResourceStream("GraduateFrequency.tsv")))
        {
            var words = new System.Collections.Generic.List<string[]>();
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                int tab = line.IndexOf('\t');
                if (tab > 0 && tab < line.Length - 1)
                    words.Add(new[] { line.Substring(0, tab), "", line.Substring(tab + 1) });
            }
            return words.ToArray();
        }
    }
    private string Answer { get { return CurrentWord[0]; } }

    public PracticeWindow(string path = null)
    {
        statePath = path ?? Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "MoyuPractice", "progress.txt");
        int savedMode;
        bool speakAutomatically;
        LoadProgress(out savedMode, out speakAutomatically);

        Text = Program.AppName;
        Icon = Program.AppIcon;
        Font = new Font("Microsoft YaHei UI", 10);
        ClientSize = new Size(640, DeckHeight);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(248, 249, 247);
        Opacity = 0.88;
        DoubleBuffered = true;
        KeyPreview = true;

        mode.DropDownStyle = ComboBoxStyle.DropDownList;
        mode.Items.AddRange(ModeNames);
        mode.SelectedIndex = savedMode;
        mode.SelectedIndexChanged += (sender, args) => { ShowWord(false); SaveProgress(); if (Visible) FocusInput(); };

        automatic.Text = "自动朗读";
        automatic.Checked = speakAutomatically;
        automatic.CheckedChanged += (sender, args) =>
        {
            SaveProgress();
            if (automatic.Checked && Visible) Speak();
            if (!automatic.Checked) StopSpeech();
            if (Visible) FocusInput();
        };
        input.Bounds = new Rectangle(-100, -100, 80, 22);
        input.ImeMode = ImeMode.Disable;
        input.ShortcutsEnabled = false;
        input.AccessibleName = "输入英文";
        input.TextChanged += (sender, args) => CheckInput();
        input.KeyDown += (sender, args) =>
        {
            if (result != Result.Pending || args.KeyCode == Keys.Left || args.KeyCode == Keys.Right ||
                args.KeyCode == Keys.Home || args.KeyCode == Keys.End)
                args.SuppressKeyPress = true;
        };
        Controls.Add(input);
        feedback.Tick += (sender, args) => FinishFeedback();
        speechTimeout.Tick += (sender, args) =>
        {
            StopSpeech();
            if (Visible) SpeakLocally();
        };
        onlineSpeech.MediaOpened += (sender, args) => speechTimeout.Stop();
        onlineSpeech.MediaEnded += (sender, args) => StopSpeech();
        onlineSpeech.MediaFailed += (sender, args) =>
        {
            if (speechFile != null && Visible)
            {
                StopSpeech();
                SpeakLocally();
            }
        };
        ShowWord(false);
    }

    public void InitializeShortcut()
    {
        if (!IsHandleCreated) CreateHandle();
    }

    public void SelectMode(int index)
    {
        if (mode.SelectedIndex == index) return;
        if (result != Result.Pending) FinishFeedback();
        mode.SelectedIndex = index;
    }

    private int DeckHeight { get { return UsingGraduate ? 160 : (UsingSentences ? 140 : 112); } }

    public void SelectDeck(Deck deck)
    {
        if (SelectedDeck != deck)
        {
            if (result != Result.Pending) FinishFeedback();
            SelectedDeck = deck;
            ClientSize = new Size(ClientSize.Width, DeckHeight);
            ShowWord(false);
            SaveProgress();
        }
    }

    public void SetAutomaticSpeech(bool enabled) { automatic.Checked = enabled; }

    public void SetTypingSound(bool enabled)
    {
        TypingSoundEnabled = enabled;
        SaveProgress();
    }

    private void PlayTypingSound()
    {
        PlaySound(ref typingSound, "TypingClick.wav");
    }

    private void PlaySound(ref SoundPlayer player, string resource)
    {
        if (!TypingSoundEnabled) return;
        try
        {
            if (player == null)
            {
                player = new SoundPlayer(typeof(PracticeWindow).Assembly.GetManifestResourceStream(resource));
                player.Load();
            }
            player.Play();
        }
        catch (InvalidOperationException) { }
    }

    private static GraphicsPath RoundedShape(Rectangle bounds, int diameter)
    {
        var shape = new GraphicsPath();
        shape.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        shape.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        shape.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        shape.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        shape.CloseFigure();
        return shape;
    }

    protected override void OnSizeChanged(EventArgs args)
    {
        base.OnSizeChanged(args);
        if (Width < 24 || Height < 24) return;
        using (var outline = RoundedShape(ClientRectangle, 24))
            Region = new Region(outline);
    }

    private static void FillPill(Graphics graphics, Rectangle bounds, Color color)
    {
        using (var shape = RoundedShape(bounds, 16))
        using (var brush = new SolidBrush(color))
            graphics.FillPath(brush, shape);
    }

    private void DrawText(Graphics graphics, string value, Font font, Rectangle box, Color color)
    {
        TextRenderer.DrawText(graphics, value, font, box, color,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private int TextWidth(Graphics graphics, string value, Font font)
    {
        // Measuring a trailing sentinel preserves spacing for a typed phrase.
        return TextRenderer.MeasureText(graphics, value + "|", font, Size.Empty, TextFormatFlags.NoPadding).Width -
            TextRenderer.MeasureText(graphics, "|", font, Size.Empty, TextFormatFlags.NoPadding).Width;
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        Graphics graphics = args.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color ink = Color.FromArgb(48, 55, 53);
        Color muted = Color.FromArgb(89, 98, 94);
        string ghost = Mask(Answer, mode.SelectedIndex);
        string typed = input.Text;
        DrawText(graphics, "×", smallFont, CloseButtonBounds, muted);
        secondPronunciationLine = Rectangle.Empty;
        if (UsingGraduate)
        {
            bool starred = graduateFavorites.Contains(GraduateIndex(0));
            int starLeft = 27 + TextWidth(graphics, ghost, spellingFont);
            graduateStar = new Rectangle(starLeft, 9, 30, 29);
            DrawText(graphics, starred ? "★" : "☆", queueFont, graduateStar,
                starred ? Color.FromArgb(142, 91, 16) : muted);
        }
        if (UsingSentences)
        {
            int split = SentenceLineBreak(graphics, ghost);
            firstPronunciationLine = PronunciationBounds(graphics, ghost.Substring(0, split), 10);
            DrawSpellingLine(graphics, ghost, typed, 0, split, 10, Width - 100, ink, muted);
            if (split < ghost.Length)
            {
                secondPronunciationLine = PronunciationBounds(graphics, ghost.Substring(split), 39);
                DrawSpellingLine(graphics, ghost, typed, split, ghost.Length, 39, Width - 40, ink, muted);
            }
            var strip = new Rectangle(12, 76, Width - 24, 32);
            FillPill(graphics, strip, ContentSurface);
            int activeWidth = Math.Min(strip.Width - 8, TextWidth(graphics, CurrentWord[2], queueFont) + 24);
            var active = new Rectangle(strip.X + 4, strip.Y + 3, activeWidth, strip.Height - 6);
            FillPill(graphics, active, ActiveSurface);
            DrawText(graphics, CurrentWord[2], queueFont, Rectangle.Inflate(active, -10, 0), Color.White);
            DrawStatus(graphics, 111);
            return;
        }
        firstPronunciationLine = PronunciationBounds(graphics, ghost, 10);
        DrawSpellingLine(graphics, ghost, typed, 0, ghost.Length, 10, 410, ink, muted);
        int ipaLeft = 32 + Math.Max(TextWidth(graphics, typed, spellingFont), TextWidth(graphics, ghost, spellingFont));
        DrawText(graphics, CurrentWord[1], smallFont, new Rectangle(ipaLeft, 11, Width - ipaLeft - 80, 28), muted);
        DrawWordQueue(graphics, ghost, ink);
        if (UsingGraduate)
        {
            FillPill(graphics, new Rectangle(12, 82, Width - 24, 47), ContentSurface);
            TextRenderer.DrawText(graphics, CurrentWord[2], smallFont,
                new Rectangle(21, 86, Width - 42, 40), ink,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
        DrawStatus(graphics, UsingGraduate ? 132 : 83);
    }

    private void DrawStatus(Graphics graphics, int top)
    {
        if (status.ForeColor == Color.Firebrick)
            FillPill(graphics, new Rectangle(12, top, Width - 24, 23), ContentSurface);
        DrawText(graphics, status.Text, smallFont,
            new Rectangle(20, top, Width - 40, 23), status.ForeColor);
    }

    private int SentenceLineBreak(Graphics graphics, string ghost)
    {
        int available = Width - 100;
        if (TextWidth(graphics, ghost, spellingFont) <= available) return ghost.Length;
        int split = ghost.LastIndexOf(' ');
        while (split > 0 && TextWidth(graphics, ghost.Substring(0, split), spellingFont) > available)
            split = ghost.LastIndexOf(' ', split - 1);
        return split > 0 ? split + 1 : ghost.Length;
    }

    private Rectangle PronunciationBounds(Graphics graphics, string spelling, int top)
    {
        return new Rectangle(15, top, Math.Max(22, TextWidth(graphics, spelling, spellingFont) + 10), 28);
    }

    private bool IsPronunciationPoint(Point point)
    {
        return firstPronunciationLine.Contains(point) || secondPronunciationLine.Contains(point);
    }

    private void DrawSpellingLine(Graphics graphics, string ghost, string answerInput,
        int start, int end, int y, int width, Color ink, Color muted)
    {
        bool lastLine = end == ghost.Length;
        string typed = answerInput.Length <= start ? "" :
            answerInput.Substring(start, (lastLine ? answerInput.Length : Math.Min(end, answerInput.Length)) - start);
        string lineHint = ghost.Substring(start, end - start);
        int x = 20;
        int typedWidth = TextWidth(graphics, typed, spellingFont);
        int remainingStart = Math.Min(typed.Length, lineHint.Length);
        DrawText(graphics, lineHint.Substring(remainingStart), spellingFont,
            new Rectangle(x + typedWidth, y, Math.Max(1, width - typedWidth), 28), muted);
        int matching = Math.Max(0, Math.Min(typed.Length, MatchingPrefixLength(answerInput) - start));
        int matchingWidth = TextWidth(graphics, typed.Substring(0, matching), spellingFont);
        Color green = Color.FromArgb(29, 116, 78);
        DrawText(graphics, typed.Substring(0, matching), spellingFont,
            new Rectangle(x, y, width, 28), result == Result.Correct ? green : ink);
        if (matching < typed.Length)
            DrawText(graphics, typed.Substring(matching), spellingFont,
                new Rectangle(x + matchingWidth, y, Math.Max(1, width - matchingWidth), 28), Color.Firebrick);
        bool caretOnLine = answerInput.Length >= start && (lastLine || answerInput.Length < end);
        if (result == Result.Correct || (input.Focused && caretOnLine))
        {
            using (var pen = new Pen(result == Result.Correct ? green : ink))
            {
                if (result == Result.Correct) graphics.DrawLine(pen, x, y + 25, x + typedWidth, y + 25);
                else graphics.DrawLine(pen, x + typedWidth, y + 4, x + typedWidth, y + 23);
            }
        }
    }

    private void DrawWordQueue(Graphics graphics, string ghost, Color ink)
    {
        var strip = new Rectangle(12, 42, Width - 24, 38);
        FillPill(graphics, strip, ContentSurface);
        string currentText = "1. " + ghost + (UsingGraduate ? "" : "  " + CurrentWord[2]);
        int currentWidth = Math.Min(238, Math.Max(153, TextWidth(graphics, currentText, queueFont) + 18));
        var current = new Rectangle(strip.X + 4, strip.Y + 4, currentWidth, strip.Height - 8);
        FillPill(graphics, current, ActiveSurface);
        DrawText(graphics, currentText, queueFont, Rectangle.Inflate(current, -8, 0), Color.White);
        int upcomingSpace = strip.Width - currentWidth - 12;
        int previewCount = UsingGraduate && !IsBrushing
            ? Math.Min(QueueLength - 1, GraduateGroupSize - GraduatePosition - 1)
            : QueueLength - 1;
        var upcoming = Enumerable.Range(1, previewCount)
            .Select(offset => (offset + 1) + ". " + WordAt(offset)[0]).ToArray();
        var naturalWidths = upcoming.Select(word => TextWidth(graphics, word, smallFont) + 16).ToArray();
        int naturalTotal = naturalWidths.Sum();
        int upcomingLeft = current.Right + 8;
        for (int offset = 1; offset <= previewCount; offset++)
        {
            int width = naturalTotal <= upcomingSpace
                ? naturalWidths[offset - 1] + (upcomingSpace - naturalTotal) / upcoming.Length
                : naturalWidths[offset - 1] * upcomingSpace / naturalTotal;
            DrawText(graphics, upcoming[offset - 1], smallFont,
                new Rectangle(upcomingLeft, strip.Y, width - 5, strip.Height), ink);
            upcomingLeft += width;
        }
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        base.OnMouseDown(args);
        if (args.Button != MouseButtons.Left) return;
        if (UsingGraduate && graduateStar.Contains(args.Location))
        {
            ToggleGraduateFavorite();
            FocusInput();
            return;
        }
        if (CloseButtonBounds.Contains(args.Location)) { ClosePopup(); return; }
        dragStart = PointToScreen(args.Location);
        dragWindowOrigin = Location;
        playOnClick = IsPronunciationPoint(args.Location);
        FocusInput();
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (dragStart.HasValue && Capture)
            MoveWithPointer(PointToScreen(args.Location));
        Cursor = dragging ? Cursors.SizeAll :
            (IsPronunciationPoint(args.Location) || CloseButtonBounds.Contains(args.Location) ||
             (UsingGraduate && graduateStar.Contains(args.Location))) ? Cursors.Hand : Cursors.SizeAll;
    }

    private void MoveWithPointer(Point pointer)
    {
        Point start = dragStart.Value;
        int dx = pointer.X - start.X;
        int dy = pointer.Y - start.Y;
        Size threshold = SystemInformation.DragSize;
        if (!dragging && Math.Abs(dx) < Math.Max(3, threshold.Width / 2) &&
            Math.Abs(dy) < Math.Max(3, threshold.Height / 2)) return;
        dragging = true;
        Location = new Point(dragWindowOrigin.X + dx, dragWindowOrigin.Y + dy);
    }

    protected override void OnMouseUp(MouseEventArgs args)
    {
        base.OnMouseUp(args);
        if (args.Button != MouseButtons.Left || !dragStart.HasValue) return;
        bool play = !dragging && playOnClick && IsPronunciationPoint(args.Location);
        EndDrag();
        Capture = false;
        if (play) Speak();
    }

    protected override void OnMouseCaptureChanged(EventArgs args)
    {
        base.OnMouseCaptureChanged(args);
        if (!Capture) EndDrag();
    }

    private void EndDrag()
    {
        if (dragging)
        {
            wasMoved = true;
            Location = FitToScreen(Location, Screen.FromRectangle(Bounds).WorkingArea, Size);
        }
        dragging = false;
        dragStart = null;
        playOnClick = false;
    }

    protected override void OnHandleCreated(EventArgs args)
    {
        base.OnHandleCreated(args);
        hotkeyRegistered = RegisterHotKey(Handle, 1, 0x4006, (uint)Keys.O);
        usingAlternateHotkey = !hotkeyRegistered;
        if (usingAlternateHotkey)
            hotkeyRegistered = RegisterHotKey(Handle, 1, 0x4006, (uint)Keys.F10);
        SetStatus();
    }

    protected override void OnHandleDestroyed(EventArgs args)
    {
        if (hotkeyRegistered) UnregisterHotKey(Handle, 1);
        base.OnHandleDestroyed(args);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312 && message.WParam.ToInt32() == 1)
        {
            if (Visible) ClosePopup();
            else Open();
            return;
        }
        base.WndProc(ref message);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys key)
    {
        if (key == Keys.Escape)
        {
            ClosePopup();
            return true;
        }
        if (key == Keys.Tab && input.Focused && (mode.SelectedIndex == 1 || mode.SelectedIndex == 2))
        {
            if (result == Result.Pending && input.TextLength < Answer.Length)
                input.AppendText(Answer[input.TextLength].ToString());
            return true;
        }
        return base.ProcessCmdKey(ref message, key);
    }

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        if (args.CloseReason == CloseReason.UserClosing)
        {
            args.Cancel = true;
            ClosePopup();
        }
        base.OnFormClosing(args);
    }

    public void Open()
    {
        if (!Visible) previousWindow = GetForegroundWindow();
        var pointer = Cursor.Position;
        var area = Screen.FromPoint(pointer).WorkingArea;
        if (wasMoved) Location = FitToScreen(Location, Screen.FromRectangle(Bounds).WorkingArea, Size);
        else if (!Visible) Location = PopupLocation(pointer, area, Size);
        Show();
        Activate();
        FocusInput();
        if (automatic.Checked) Speak();
    }

    private static Point PopupLocation(Point pointer, Rectangle area, Size size)
    {
        return FitToScreen(new Point(pointer.X + 12, pointer.Y + 12), area, size);
    }

    private static Point FitToScreen(Point position, Rectangle area, Size size)
    {
        return new Point(
            Math.Max(area.Left, Math.Min(position.X, area.Right - size.Width)),
            Math.Max(area.Top, Math.Min(position.Y, area.Bottom - size.Height)));
    }

    public void ClosePopup()
    {
        EndDrag();
        Capture = false;
        Hide();
        StopSpeech();
        if (result != Result.Pending) FinishFeedback();
        if (previousWindow != IntPtr.Zero) SetForegroundWindow(previousWindow);
        previousWindow = IntPtr.Zero;
    }

    private void FocusInput()
    {
        ActiveControl = input;
        input.Focus();
        input.SelectionStart = input.TextLength;
        Invalidate();
    }

    private void ShowWord(bool readAloud)
    {
        ResetInput("");
        if (readAloud && automatic.Checked) Speak();
    }

    private void ResetInput(string retainedText)
    {
        updating = true;
        feedback.Stop();
        result = Result.Pending;
        input.ReadOnly = false;
        input.Text = retainedText;
        input.Select(input.TextLength, 0);
        updating = false;
        SetStatus();
    }

    private int MatchingPrefixLength(string typed)
    {
        int matching = 0;
        while (matching < typed.Length && matching < Answer.Length &&
            char.ToUpperInvariant(typed[matching]) == char.ToUpperInvariant(Answer[matching]))
            matching++;
        return matching;
    }

    private string CompletedWords(string typed)
    {
        int matching = MatchingPrefixLength(typed);
        // A wrong separator should not erase the correctly spelled word before it.
        if (matching < Answer.Length && Answer[matching] == ' ')
            return typed.Substring(0, matching);
        int separator = typed.Substring(0, matching).LastIndexOf(' ');
        return typed.Substring(0, separator + 1);
    }

    private static string Mask(string word, int selectedMode)
    {
        if (selectedMode == 0 || selectedMode == 3) return word;
        return new string(word.Select((letter, index) =>
            !char.IsLetter(letter) || (selectedMode == 1 && index % 2 == 0) ? letter : '_').ToArray());
    }

    private void SetStatus(string message = null)
    {
        string shortcut = usingAlternateHotkey ? "Ctrl+Shift+F10" : "Ctrl+Shift+O";
        string keys = hotkeyRegistered ? shortcut + " 开关 · Esc 收起" : "快捷键被占用，可从托盘打开";
        string hint = mode.SelectedIndex == 1 || mode.SelectedIndex == 2 ? " · Tab 提示" : "";
        status.ForeColor = message == null ? Color.FromArgb(91, 101, 97) : Color.Firebrick;
        string deckName = UsingGraduate ? "考研词频" : (UsingSentences ? "句子" : "词库");
        string progress = UsingGraduate && !IsBrushing ? GraduateProgress :
            deckName + " · 已完成 " + CurrentCount;
        status.Text = message ?? (progress + " 次  ·  " + keys + hint);
        Invalidate();
    }

    private void CheckInput()
    {
        if (updating || result != Result.Pending) return;
        if (!Answer.StartsWith(input.Text, StringComparison.OrdinalIgnoreCase))
        {
            result = Result.Incorrect;
            input.ReadOnly = true;
            PlaySound(ref incorrectSound, "Incorrect.wav");
            if (automatic.Checked) Speak();
            SetStatus(CompletedWords(input.Text).Length == 0
                ? "输错了，稍后清空重试"
                : "输错了，稍后从当前单词重试");
            feedback.Interval = 520;
            feedback.Start();
            return;
        }
        if (input.TextLength != Answer.Length)
        {
            PlayTypingSound();
            SetStatus();
            return;
        }
        result = Result.Correct;
        input.ReadOnly = true;
        PlaySound(ref correctSound, "Correct.wav");
        SetStatus();
        feedback.Interval = 230;
        feedback.Start();
    }

    private void FinishFeedback(bool allowSpeech = true)
    {
        if (result == Result.Incorrect)
        {
            ResetInput(CompletedWords(input.Text));
            return;
        }
        if (result != Result.Correct) return;
        if (UsingGraduate)
        {
            if (IsBrushing) graduateBrushed++;
            else
            {
                EnsureGraduateGroup();
                graduateCompleted++;
                if (GraduatePosition == 0)
                {
                    graduateGroupStart = graduateNext;
                    graduateGroup = null;
                }
            }
        }
        else if (UsingSentences)
        {
            if (IsBrushing) sentencesBrushed++;
            else sentencesCompleted++;
        }
        else if (IsBrushing) brushed++;
        else completed++;
        ShowWord(false);
        SaveProgress();
        if (allowSpeech && Visible && automatic.Checked) Speak();
    }

    private void Speak()
    {
        StopSpeech();
        string term = Answer;
        if (UsingSentences || !System.Text.RegularExpressions.Regex.IsMatch(term, @"^[a-z][a-z'’ -]{0,149}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            SpeakLocally();
            return;
        }

        int version = speechVersion;
        var request = new PronunciationClient();
        speechRequest = request;
        request.DownloadDataCompleted += (sender, args) =>
        {
            if (version != speechVersion || IsDisposed) return;
            speechRequest = null;
            request.Dispose();
            if (args.Cancelled || !Visible || Answer != term) return;
            if (args.Error == null && IsMp3(args.Result))
            {
                try
                {
                    speechFile = Path.Combine(Path.GetTempPath(), "MoyuSpeech-" + Guid.NewGuid().ToString("N") + ".mp3");
                    File.WriteAllBytes(speechFile, args.Result);
                    onlineSpeech.Open(new Uri(speechFile));
                    onlineSpeech.Play();
                    return;
                }
                catch (IOException) { StopSpeech(); }
                catch (UnauthorizedAccessException) { StopSpeech(); }
            }
            SpeakLocally();
        };
        try
        {
            request.DownloadDataAsync(new Uri("https://dict.youdao.com/dictvoice?type=2&audio=" + Uri.EscapeDataString(term)));
            speechTimeout.Start();
        }
        catch (System.Net.WebException)
        {
            StopSpeech();
            SpeakLocally();
        }
    }

    private sealed class PronunciationClient : System.Net.WebClient
    {
        protected override System.Net.WebRequest GetWebRequest(Uri address)
        {
            var request = (System.Net.HttpWebRequest)base.GetWebRequest(address);
            request.Accept = "audio/mpeg";
            request.Timeout = 4000;
            request.ReadWriteTimeout = 4000;
            return request;
        }
    }

    private static bool IsMp3(byte[] audio)
    {
        return audio.Length >= 4 && audio.Length <= 1024 * 1024 &&
            ((audio[0] == 'I' && audio[1] == 'D' && audio[2] == '3') ||
            (audio[0] == 255 && (audio[1] & 224) == 224));
    }

    private void StopSpeech()
    {
        speechTimeout.Stop();
        speechVersion++;
        if (speechRequest != null)
        {
            speechRequest.CancelAsync();
            speechRequest.Dispose();
            speechRequest = null;
        }
        if (speech != null) speech.SpeakAsyncCancelAll();
        onlineSpeech.Close();
        if (speechFile == null) return;
        try { File.Delete(speechFile); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        speechFile = null;
    }

    private void SpeakLocally()
    {
        try
        {
            if (speech == null)
            {
                speech = new SpeechSynthesizer();
                var voice = speech.GetInstalledVoices().FirstOrDefault(item =>
                    item.Enabled && item.VoiceInfo.Culture.TwoLetterISOLanguageName == "en");
                if (voice != null) speech.SelectVoice(voice.VoiceInfo.Name);
            }
            speech.SpeakAsyncCancelAll();
            speech.SpeakAsync(Answer);
        }
        catch (InvalidOperationException) { SetStatus("系统语音无法播放"); }
        catch (COMException) { SetStatus("系统语音无法播放"); }
    }

    private void LoadProgress(out int savedMode, out bool speakAutomatically)
    {
        savedMode = 0;
        speakAutomatically = true;
        TypingSoundEnabled = true;
        try
        {
            if (!File.Exists(statePath)) return;
            var lines = File.ReadAllLines(statePath);
            int value;
            if (lines.Length > 0 && int.TryParse(lines[0], out value) && value >= 0)
                completed = value;
            if (lines.Length > 1 && int.TryParse(lines[1], out value) && value >= 0 && value < modeCount)
                savedMode = value;
            bool enabled;
            if (lines.Length > 2 && bool.TryParse(lines[2], out enabled))
                speakAutomatically = enabled;
            if (lines.Length > 3 && bool.TryParse(lines[3], out enabled))
                TypingSoundEnabled = enabled;
            if (lines.Length > 4 && int.TryParse(lines[4], out value) && value >= 0)
                sentencesCompleted = value;
            if (lines.Length > 5 && bool.TryParse(lines[5], out enabled))
                SelectedDeck = enabled ? Deck.Sentences : Deck.Words;
            if (lines.Length > 6 && int.TryParse(lines[6], out value) && value >= 0)
                graduateCompleted = value;
            if (lines.Length > 7 && bool.TryParse(lines[7], out enabled) && enabled)
                SelectedDeck = Deck.Graduate;
            graduateGroupStart = graduateCompleted - GraduatePosition;
            if (lines.Length > 8)
                foreach (int index in ParseGraduateIndices(lines[8])) graduateFavorites.Add(index);
            if (lines.Length > 9 && int.TryParse(lines[9], out value) && value >= 0)
                graduateGroupStart = value;
            if (lines.Length > 11 && int.TryParse(lines[11], out value) && value > graduateGroupStart)
            {
                var saved = ParseGraduateIndices(lines[10]);
                if (saved.Length == GraduateGroupSize && saved.Distinct().Count() == GraduateGroupSize)
                {
                    graduateGroup = saved;
                    graduateNext = value;
                }
            }
            if (lines.Length > 12 && int.TryParse(lines[12], out value) && value >= 0)
                brushed = value;
            if (lines.Length > 13 && int.TryParse(lines[13], out value) && value >= 0)
                sentencesBrushed = value;
            if (lines.Length > 14 && int.TryParse(lines[14], out value) && value >= 0)
                graduateBrushed = value;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private const int modeCount = 4;

    private static int[] ParseGraduateIndices(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new int[0];
        int max = GraduateWords.Value.Length;
        var result = new System.Collections.Generic.List<int>();
        foreach (string piece in text.Split(','))
        {
            int index;
            if (int.TryParse(piece, out index) && index >= 0 && index < max) result.Add(index);
        }
        return result.ToArray();
    }

    private void SaveProgress()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(statePath));
            string pending = statePath + ".tmp";
            File.WriteAllLines(pending, new[] { completed.ToString(), mode.SelectedIndex.ToString(),
                automatic.Checked.ToString(), TypingSoundEnabled.ToString(),
                sentencesCompleted.ToString(), UsingSentences.ToString(),
                graduateCompleted.ToString(), UsingGraduate.ToString(),
                string.Join(",", graduateFavorites.OrderBy(index => index)), graduateGroupStart.ToString(),
                graduateGroup == null ? "" : string.Join(",", graduateGroup), graduateNext.ToString(),
                brushed.ToString(), sentencesBrushed.ToString(), graduateBrushed.ToString() });
            if (File.Exists(statePath)) File.Replace(pending, statePath, null);
            else File.Move(pending, statePath);
        }
        catch (IOException) { SetStatus("进度无法保存"); }
        catch (UnauthorizedAccessException) { SetStatus("进度无法保存"); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (result == Result.Correct) FinishFeedback(false);
            StopSpeech();
            speechTimeout.Dispose();
            feedback.Dispose();
            spellingFont.Dispose();
            queueFont.Dispose();
            smallFont.Dispose();
            mode.Dispose();
            automatic.Dispose();
            status.Dispose();
            if (speech != null) speech.Dispose();
            foreach (var player in new[] { typingSound, correctSound, incorrectSound })
            {
                if (player == null) continue;
                var soundStream = player.Stream;
                player.Dispose();
                if (soundStream != null) soundStream.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    internal static int TestBrushRestart(string path, bool save)
    {
        using (var form = new PracticeWindow(path))
        {
            if (!save)
                return form.SelectedDeck == Deck.Graduate && form.SelectedMode == 3 &&
                    form.Answer == "use" && form.graduateBrushed == 2 && form.graduateCompleted == 7 &&
                    form.graduateFavorites.Contains(1) ? 0 : 46;
            if (form.SelectedDeck != Deck.Graduate || form.SelectedMode != 3 || form.Answer != "be") return 45;
            form.input.Text = "be";
            form.FinishFeedback();
            form.input.Text = "do";
        }
        return 0;
    }

    public static int SelfTest(string screenshot)
    {
        if (!IsMp3(new byte[] { 73, 68, 51, 4 }) || IsMp3(new byte[] { 60, 104, 116, 109 }))
            return 35;
        if (Mask("account for", 2) != "_______ ___" || Mask("test", 1) != "t_s_")
            return 1;
        var location = PopupLocation(new Point(998, 798), new Rectangle(0, 0, 1000, 800), new Size(445, 230));
        if (location != new Point(555, 570)) return 2;
        var path = Path.Combine(Path.GetTempPath(), "MoyuPracticeTest-" + Guid.NewGuid() + ".txt");
        try
        {
            File.WriteAllLines(path, new[] { "0", "0", "True" });
            using (var form = new PracticeWindow(path))
            {
                if (!form.TypingSoundEnabled) return 18;
                form.PlayTypingSound();
                if (form.typingSound == null || !form.typingSound.IsLoadCompleted) return 19;
                form.automatic.Checked = false;
                form.mode.SelectedIndex = 2;
                form.input.Text = "ax";
                if (form.incorrectSound == null || !form.incorrectSound.IsLoadCompleted) return 22;
                if (form.result != Result.Incorrect || form.input.Text != "ax") return 9;
                form.FinishFeedback();
                if (form.input.TextLength != 0 || form.completed != 0) return 10;
                form.input.Text = "ACCOUNT fx";
                form.FinishFeedback();
                if (form.input.Text != "ACCOUNT " || form.input.SelectionStart != 8 ||
                    form.completed != 0 || form.input.ReadOnly) return 14;
                form.input.AppendText("FOR");
                if (form.correctSound == null || !form.correctSound.IsLoadCompleted) return 23;
                form.SetTypingSound(false);
                if (form.result != Result.Correct || form.Answer != "account for") return 11;
                form.FinishFeedback();
                if (form.completed != 1 || form.input.TextLength != 0 || form.Answer != "substantial")
                    return 3;
                if (form.WordAt(4)[0] != "in contrast") return 12;
            }
            using (var form = new PracticeWindow(path))
            {
                if (form.completed != 1 || form.mode.SelectedIndex != 2 || form.automatic.Checked)
                    return 4;
                if (form.TypingSoundEnabled) return 20;
                form.PlayTypingSound();
                if (form.typingSound != null) return 21;
                form.mode.SelectedIndex = 3;
                form.input.Text = "account for";
                form.FinishFeedback();
                if (form.brushed != 1 || form.completed != 1 || form.Answer != "substantial")
                    return 5;
                form.Open();
                Application.DoEvents();
                if (!form.Visible || !form.hotkeyRegistered)
                    return 6;
                Point originalLocation = form.Location;
                form.OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 25, 95, 0));
                Point? start = form.dragStart;
                if (!start.HasValue) return 48;
                Point pointer = start.Value;
                form.MoveWithPointer(new Point(pointer.X + 1, pointer.Y + 1));
                if (form.Location != originalLocation) return 49;
                form.MoveWithPointer(new Point(pointer.X + 40, pointer.Y + 30));
                if (form.Location != new Point(originalLocation.X + 40, originalLocation.Y + 30))
                    return 50;
                form.OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 25, 95, 0));
                Point movedLocation = form.Location;
                form.ClosePopup();
                form.Open();
                if (form.Location != movedLocation || form.dragStart != null) return 51;
                if (form.Opacity >= 1 || form.Region == null ||
                    form.Region.IsVisible(0, 0) || !form.Region.IsVisible(320, 56))
                    return 24;
                form.input.Text = "x";
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (form.result != Result.Pending && wait.ElapsedMilliseconds < 2000)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(10);
                }
                if (form.result != Result.Pending || form.input.TextLength != 0) return 13;
                if (screenshot != null)
                {
                    form.input.Text = "sub";
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, form.ClientRectangle);
                        bitmap.Save(screenshot, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                var hotkey = Message.Create(form.Handle, 0x0312, new IntPtr(1), IntPtr.Zero);
                form.WndProc(ref hotkey);
                if (form.Visible) return 7;
                form.WndProc(ref hotkey);
                if (!form.Visible) return 8;
                form.ClosePopup();
                form.mode.SelectedIndex = 2;
                form.completed = 3;
                form.ShowWord(false);
                form.input.Text = "take ix";
                form.FinishFeedback();
                if (form.input.Text != "take " || form.completed != 3) return 15;
                form.input.AppendText("into ax");
                form.FinishFeedback();
                if (form.input.Text != "take into " || form.completed != 3) return 16;
                form.input.AppendText("account");
                form.FinishFeedback();
                if (form.completed != 4 || form.Answer != "contribute") return 17;
                form.SelectDeck(Deck.Sentences);
                form.Open();
                if (!form.UsingSentences || form.sentencesCompleted != 0) return 25;
                form.mode.SelectedIndex = 0;
                form.input.Text = "Small firms can no longx";
                form.FinishFeedback();
                if (form.input.Text != "Small firms can no ") return 26;
                form.input.AppendText("longer operate independently of major platforms");
                if (form.result != Result.Correct) return 27;
                form.FinishFeedback();
                if (form.sentencesCompleted != 1 || form.completed != 4 || form.Answer != Sentences[1][0])
                    return 28;
                using (var graphics = form.CreateGraphics())
                {
                    foreach (var sentence in Sentences)
                    {
                        int split = form.SentenceLineBreak(graphics, sentence[0]);
                        if (form.TextWidth(graphics, sentence[0].Substring(split), form.spellingFont) > form.Width - 40)
                            return 29;
                    }
                }
                if (screenshot != null)
                {
                    form.input.Text = "The report";
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, form.ClientRectangle);
                        bitmap.Save(Path.ChangeExtension(screenshot, ".sentence.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    if (!form.IsPronunciationPoint(new Point(20, 20)) ||
                        !form.IsPronunciationPoint(new Point(20, 50)) ||
                        form.IsPronunciationPoint(new Point(20, 90))) return 43;
                }
                form.ClosePopup();
            }
            using (var form = new PracticeWindow(path))
            {
                if (!form.UsingSentences || form.Answer != Sentences[1][0] || form.ClientSize.Height != 140)
                    return 30;
                form.SelectDeck(Deck.Words);
                if (form.Answer != "contribute" || form.sentencesCompleted != 1) return 31;
                form.SelectDeck(Deck.Graduate);
                form.Open();
                if (GraduateWords.Value.Length < 51 || form.Answer != GraduateWords.Value[0][0] || form.CurrentWord[2].Length == 0)
                    return 32;
                form.input.Text = GraduateWords.Value[0][0];
                form.FinishFeedback();
                if (form.Answer != GraduateWords.Value[1][0] || form.graduateCompleted != 1 ||
                    form.completed != 4 || form.sentencesCompleted != 1) return 33;
                if (screenshot != null)
                {
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, form.ClientRectangle);
                        bitmap.Save(Path.ChangeExtension(screenshot, ".graduate.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Rectangle starBounds = form.graduateStar;
                    if (!form.IsPronunciationPoint(new Point(20, 20)) ||
                        form.IsPronunciationPoint(starBounds.Location) ||
                        form.IsPronunciationPoint(form.CloseButtonBounds.Location)) return 44;
                }
                form.ClosePopup();
            }
            using (var form = new PracticeWindow(path))
            {
                if (!form.UsingGraduate || form.Answer != GraduateWords.Value[1][0] || form.ClientSize.Height != 160) return 34;
                form.InitializeShortcut();
                if (!form.hotkeyRegistered || form.Visible) return 36;
                using (var management = new ManagementWindow(form))
                {
                    management.Show();
                    Application.DoEvents();
                    if (!management.VerifyControls()) return 37;
                    if (screenshot != null)
                    {
                        using (var bitmap = new Bitmap(management.Width, management.Height))
                        {
                            management.DrawToBitmap(bitmap, new Rectangle(Point.Empty, management.Size));
                            bitmap.Save(Path.ChangeExtension(screenshot, ".management.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                }
            }
            using (var form = new PracticeWindow(path))
            {
                if (form.SelectedDeck != Deck.Sentences || form.SelectedMode != 1 ||
                    !form.AutomaticSpeech || !form.TypingSoundEnabled || form.graduateCompleted != 1)
                    return 38;
                form.SetAutomaticSpeech(false);
                form.SetTypingSound(false);
                form.SelectDeck(Deck.Graduate);
                form.ToggleGraduateFavorite();
                if (!form.graduateFavorites.Contains(1) || form.Answer != GraduateWords.Value[1][0]) return 39;
                form.graduateCompleted = 49;
                form.ShowWord(false);
                form.input.Text = form.Answer;
                form.FinishFeedback();
                if (form.GraduatePosition != 0 || form.Answer != GraduateWords.Value[1][0] ||
                    form.graduateGroup.Distinct().Count() != 50 || form.GraduateIndex(1) != 50)
                    return 40;
                form.SaveProgress();
            }
            using (var form = new PracticeWindow(path))
            {
                if (!form.graduateFavorites.Contains(1) || form.Answer != GraduateWords.Value[1][0] || form.graduateCompleted != 50)
                    return 41;
                form.ToggleGraduateFavorite();
                if (form.graduateFavorites.Contains(1) || form.Answer != GraduateWords.Value[1][0]) return 42;
            }
            return 0;
        }
        finally
        {
            File.Delete(path);
        }
    }
}

