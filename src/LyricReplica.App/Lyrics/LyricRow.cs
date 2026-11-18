extern alias emoji;
using EmojiTextBlock = emoji::Emoji.Wpf.TextBlock;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using LyricReplica.Core;
using LyricReplica.App.Reference;
using Timeline = LyricReplica.Core.Timeline;

namespace LyricReplica.App;

public sealed class LyricRow : Grid
{
    private static readonly double[] BlurRadii = [0, 7, 12, 16, 16];
    private static readonly BitmapCache FractionalLiftCache = MotionTokens.Freeze(new BitmapCache { SnapsToDevicePixels = false });
    private static readonly BitmapCache HanLiftCache = MotionTokens.Freeze(new BitmapCache { SnapsToDevicePixels = false, RenderAtScale = 1.5 });
    internal bool UseFractionalLift { get; init; }
    private sealed record Glyph(TextBlock View, ScaleTransform Scale, TranslateTransform Move, int Index, int Count, BitmapCache LiftCache)
    {
        public double PrepareFromY
        {
            get; set;
        }
        public double? EarlyFromY
        {
            get; set;
        }
        public double? HeldY
        {
            get; set;
        }
        public double? HeldScale
        {
            get; set;
        }
        public double? StartScale
        {
            get; set;
        }
        public double? ResetFromY
        {
            get; set;
        }
    }
    private sealed record FloatView(TextBlock View, TranslateTransform Move, double Start, double Duration, bool Syllable, BitmapCache LiftCache)
    {
        public double PrepareFromY
        {
            get; set;
        }
        public double? EarlyFromY
        {
            get; set;
        }
        public double? HeldY
        {
            get; set;
        }
        public double? ResetFromY
        {
            get; set;
        }
    }
    private sealed record WordView(int Index, LyricWord Word, double X, double Width, double InkWidth, IReadOnlyList<Glyph> Glyphs, Canvas Host, IReadOnlyList<FloatView> Floats)
    {
        public LyricWord? MotionWord
        {
            get; set;
        }
        public double? ClockStartedMs
        {
            get; set;
        }
        public double? DispatchStartedMs
        {
            get; set;
        }
        public double? ResetStoppedAtMs
        {
            get; set;
        }
    }
    private sealed record Strip(Canvas Canvas, double Width, LinearGradientBrush Mask, IReadOnlyList<WordView> Words, WrappedLine Layout)
    {
        public double Progress
        {
            get; set;
        }
        public double FinishFrom
        {
            get; set;
        }
        public WordSweepPlan? Sweep
        {
            get; set;
        }
        public double? SweepClockStartedMs
        {
            get; set;
        }
        public LineAnimationPlan? ColorAnimation
        {
            get; set;
        }
        public double ColorAnimationStartedMs
        {
            get; set;
        }
        public bool Frozen
        {
            get; set;
        }
        public (Color Played, Color Unplayed)? SavedColors
        {
            get; set;
        }
    }
    private readonly LyricLine _line;
    private readonly bool _right, _background, _above, _karaoke, _pageKaraoke;
    private readonly ViewOptions _options;
    private readonly List<Strip> _strips = [];
    private WordRunState _wordRun = new();
    private LineAnimationPlan? _lineAnimation;
    private BackgroundAnimationPlan? _backgroundAnimation;
    private BackgroundFrame _backgroundFrame;
    internal double ParentContentOffsetY => _backgroundFrame.ParentOffsetY;
    internal (bool UseBackEase, double GapSeconds) BackgroundExitTransition { get; set; } = (false, .2);
    private double _lineAnimationStartedMs;
    internal double SwitchDelayMs
    {
        get; set;
    }
    private double _backgroundDelayMs;
    private (double Start, double Duration, double Height, bool Ios)? _overlapBottom;
    private BackgroundFrame _backgroundBeforeDelay;
    internal bool BackgroundOverlap
    {
        get; set;
    }
    private bool _backgroundRight;
    private bool? _linePlaying;
    private double? _finishClockStartedMs;
    private double? _prepareClockStartedMs, _stoppedClockStartedMs;
    private double _prepareLyricTime;
    private sealed class WordResetState
    {
        public bool Blocked; public int Sequence; public double ClockStartedMs;
    }
    private WordResetState _wordReset = new();
    private int _appliedResetSequence;
    private bool _applyingWordReset, _resumeResetWords;
    private double? _resetClockStartedMs;
    private double _lastAnimationTimeMs;
    private (double Time, double RowTime, bool Active, int Distance, double Start, double End, long Generation)? _lastUpdate;
    internal Func<double>? AnimationClockNow
    {
        get; set;
    }
    internal double NextLineStartMs { get; set; } = -1;
    internal bool FinishingWords => _wordRun.Finishing;
    internal int RunningWord => _wordRun.CurrentWord;
    private readonly UserControl _view;
    private readonly Grid _outside;
    private readonly Thickness _outsideMargin;
    private readonly StackPanel _content;
    private readonly StackPanel _glyphHost = new();
    private readonly TextBlock _plainText;
    private readonly List<(LyricRow Row, bool Above)> _backingRows = [];
    private readonly TranslateTransform _rootMove = new();
    private readonly List<TextBlock> _secondary = [];
    private readonly ScaleTransform _rootScale = new(1, 1), _contentScale = new(1, 1);
    private readonly BlurEffect _blur = new() { Radius = 0, RenderingBias = RenderingBias.Performance };
    private readonly Typeface _typeface;
    private readonly FontFamily _family;
    private readonly double _fontSize, _scale;
    private readonly double _feather;
    private readonly double _dpi;
    private double _lastBlur = -1;
    private bool _hoverBlurRestored;
    private bool _armed;
    private readonly RowFeedback? _feedback;
    internal Func<bool> HoverAllowed { get; set; } = () => true;
    private readonly Dictionary<string, TextMeasure> _measures = new(StringComparer.Ordinal);
    public event Action<double>? SeekRequested;
    public event Action<LyricLine, CopyKind>? CopyRequested;
    public LyricLine Line => _line;
    public LineFrame Frame
    {
        get; private set;
    }
    public bool SlotOpen
    {
        get; private set;
    }
    public double TextWidth
    {
        get;
    }
    public bool HaveAnyTranslation
    {
        get; set;
    }
    public double NaturalHeight => Math.Max(1, _view.ActualHeight > 0 ? _view.ActualHeight : _view.DesiredSize.Height);

    public LyricRow(LyricLine line, bool right, bool background, double width, double displayScale, FontFamily family, ViewOptions options, bool duetIndent = false, bool above = false, bool portrait = false, bool karaoke = true, bool haveRomanization = false)
    {
        _line = line;
        _right = right;
        _background = background;
        _above = above;
        _pageKaraoke = karaoke;
        _options = options;
        _family = family;
        _backgroundRight = background && options.AppleMusicLyrics.BuggyApple && options.AppleMusicLyrics.BuggyAppleOptions.BackgroundVocalsEnteringDirection && Random.Shared.Next(10) > 7;
        SingOpacityDuration = options.AppleMusicLyrics.LineSwitchingAppleIosStyle164 ? 700 : 350;
        _karaoke = karaoke && options.AppleMusicLyrics.Karaoke && line.Words.Count > 0;
        _scale = displayScale;
        _fontSize = (background ? 26 : _karaoke ? 41 : 42) * displayScale;
        _dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        _feather = options.AppleMusicLyrics.GetProgressWidth(portrait);
        _typeface = new Typeface(family, FontStyles.Normal, options.Bold ? FontWeights.Heavy : FontWeights.Bold, FontStretches.Normal);
        Background = Brushes.Transparent;
        Cursor = background ? Cursors.Arrow : Cursors.Hand;
        Focusable = !background;
        ClipToBounds = false;
        RenderTransformOrigin = new Point(right ? 1 : 0, .5);
        var transforms = new TransformGroup();
        transforms.Children.Add(_rootScale);
        transforms.Children.Add(_rootMove);
        RenderTransform = transforms;
        _view = (UserControl)Application.LoadComponent(new Uri("/LyricReplica;component/Assets/ReferenceLine.xaml", UriKind.Relative));
        _view.FontFamily = family;
        _view.RenderTransform = Transform.Identity;
        Children.Add(_view);
        _outside = (Grid)_view.FindName("GridOutside");
        _content = (StackPanel)_view.FindName("StackPanelLyrics");
        _plainText = (TextBlock)_view.FindName("TextBlockLyrics");
        _content.Effect = _blur;
        _content.RenderTransform = _contentScale;
        _content.RenderTransformOrigin = new Point(right ? 1 : 0, .5);
        _content.Margin = background ? new Thickness(20 * _scale, above ? 0 : 17 * _scale, 20 * _scale, above ? 17 * _scale : 0) : new Thickness(20 * _scale, 15 * _scale, 20 * _scale, 15 * _scale);
        if (background)
        {
            _outside.Margin = above ? new Thickness(0, -3 * _scale, 0, 0) : new Thickness(0, 0, 0, -3 * _scale);
            ((Grid)_view.FindName("MainGrid")).Margin = above ? new Thickness(-20 * _scale, 3 * _scale, -20 * _scale, 0) : new Thickness(-20 * _scale, 0, -20 * _scale, 3 * _scale);
            VerticalAlignment = VerticalAlignment.Top;
        }
        double inset = duetIndent && line.AlignLeft.HasValue ? 76 * _scale : 0;
        if (inset > 0)
            _outside.Margin = right ? new Thickness(inset, 0, 0, 0) : new Thickness(0, 0, inset, 0);
        _outsideMargin = _outside.Margin;
        var spacing = (GridSpacing)_view.FindName("GridForSpacing");
        spacing.Spacing = !_karaoke ? .09 : options.AppleMusicLyrics.HighlightLineZooming switch
        {
            2 => .08,
            3 => .04,
            _ => .02
        };
        if (right)
            Grid.SetColumn(spacing, 0);
        TextWidth = Math.Max(60, (width - inset + (background ? 40 * _scale : 0)) * (1 - spacing.Spacing) - 40 * _scale);
        double available = TextWidth;
        _plainText.FontSize = _fontSize;
        _plainText.FontWeight = options.Bold ? FontWeights.Heavy : FontWeights.Bold;
        _plainText.LineHeight = 49 * _scale * (background ? 26d / 42 : 1);
        _plainText.TextAlignment = right ? TextAlignment.Right : TextAlignment.Left;
        _plainText.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        var sing = (ContentControl)_view.FindName("TextSing");
        sing.Content = _glyphHost;
        sing.Padding = new Thickness(0, 0, 0, -4 * _scale);
        if (!_karaoke)
        {
            sing.Visibility = Visibility.Collapsed;
            _plainText.Text = line.Text;
        }
        else
        {
            _plainText.Visibility = Visibility.Collapsed;
            foreach (var wrapped in LineBreaker.Wrap(line.Words, available, Measure))
                BuildStrip(wrapped, available);
        }
        AddSecondary("TextBlockRomaji", options.Romanization ? line.Romanization : "", 24, 32, background ? new Thickness(0, 3 * _scale, 0, -3 * _scale) : new Thickness(0, 4 * _scale, 0, -5.5 * _scale));
        AddSecondary("TextBlockTranslation", options.Translation && !(options.RomajiOption == RomajiOptions.RomajiOnly && (haveRomanization || line.Romanization.Length > 0)) ? line.Translation : "", 24, 38, background ? new Thickness(0, 7 * _scale, 0, -3 * _scale) : new Thickness(0, 11 * _scale, 0, -5 * _scale));
        _view.Measure(new Size(width, double.PositiveInfinity));
        if (background)
            Margin = new Thickness(0, -NaturalHeight, 0, 0);
        if (!background)
        {
            var menu = (ContextMenu)_view.FindName("MenuRightClick");
            ContextMenu = menu;
            menu.FontFamily = family;
            ReferenceMenus.Apply(menu);
            foreach (var (name, kind) in new[] { ("MenuItemCopy", CopyKind.Line), ("MenuItemCopyTranslation", CopyKind.Translation), ("MenuItemCopyLineWithTranslation", CopyKind.LineWithTranslation), ("MenuItemCopyAllLines", CopyKind.AllLines), ("MenuItemCopyAllTranslations", CopyKind.AllTranslations), ("MenuItemCopyAllLinesWithTranslation", CopyKind.AllWithTranslations) })
                ((MenuItem)_view.FindName(name)).Click += (_, _) => CopyRequested?.Invoke(_line, kind);
            var share = (MenuItem)_view.FindName("MenuItemShare");
            share.IsEnabled = false;
            share.ToolTip = "当前播放源未提供分享窗口";
            menu.Opened += (_, _) => RefreshCopyMenu();
            _feedback = new RowFeedback(this, (Grid)_view.FindName("MainGrid"), (Border)_view.FindName("BorderBackground"), () => !_options.ReduceMotion, () => HoverAllowed());
            MouseLeftButtonDown += (_, e) => { _armed = true; Focus(); e.Handled = true; };
            MouseLeftButtonUp += async (_, e) => { if (_armed) { _armed = false; SeekRequested?.Invoke(line.Words.FirstOrDefault()?.StartMs ?? line.StartMs); e.Handled = true; if (_karaoke && SlotOpen) await ResetCurrentWordAsync(); } };
            MouseLeave += (_, _) => _armed = false;
            KeyDown += (_, e) => { if (e.Key == Key.Enter) { SeekRequested?.Invoke(line.StartMs); e.Handled = true; } };
        }
        System.Windows.Automation.AutomationProperties.SetName(this, line.Text);
    }
    public void AddBackground(LyricRow row, bool above)
    {
        if (above)
            _content.Children.Insert(_backingRows.Count(x => x.Above), row);
        else
            _content.Children.Add(row);
        _backingRows.Add((row, above));
    }
    public void RefreshCopyMenu()
    {
        foreach (string name in new[] { "MenuItemCopyTranslation", "MenuItemCopyLineWithTranslation" })
            ((MenuItem)_view.FindName(name)).Visibility = _options.Translation && !string.IsNullOrWhiteSpace(_line.Translation) ? Visibility.Visible : Visibility.Collapsed;
        foreach (string name in new[] { "MenuItemCopyAllTranslations", "MenuItemCopyAllLinesWithTranslation" })
            ((MenuItem)_view.FindName(name)).Visibility = _options.Translation && HaveAnyTranslation ? Visibility.Visible : Visibility.Collapsed;
    }

    private TextBlock Text(string value, double size, FontWeight weight) => new()
    {
        Text = value,
        FontSize = size,
        FontWeight = weight,
        FontFamily = _family,
        Foreground = Brushes.White,
        TextWrapping = TextWrapping.NoWrap,
        ClipToBounds = false,
        SnapsToDevicePixels = false
    };
    private TextMeasure Measure(string value)
    {
        if (_measures.TryGetValue(value, out var cached))
            return cached;
        var t = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _typeface, _fontSize, Brushes.White, _dpi);
        var block = Text(value, _fontSize, _options.Bold ? FontWeights.Heavy : FontWeights.Bold);
        block.MinHeight = 50 * _fontSize / 41;
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var result = new TextMeasure(t.WidthIncludingTrailingWhitespace, t.Width, block.DesiredSize.Height);
        _measures[value] = result;
        return result;
    }
    private void AddSecondary(string name, string value, double size, double height, Thickness margin)
    {
        var text = (TextBlock)_view.FindName(name);
        if (name == "TextBlockTranslation" && _options.EnableColorfulEmoji && emoji::Emoji.Wpf.EmojiData.MatchOne.IsMatch(value))
        {
            int index = _content.Children.IndexOf(text);
            _content.Children.RemoveAt(index);
            text = new EmojiTextBlock { FontFamily = _family, Foreground = Brushes.White };
            _content.Children.Insert(index, text);
        }
        if (text is EmojiTextBlock emojiText)
            emojiText.Text = value;
        else
            text.Text = value;
        text.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        bool small = _background && _options.AppleMusicLyrics.TranslationLinePreset != TranslationLinePresetTypes.iOS;
        text.FontSize = (small ? 18 : size) * _scale;
        text.FontWeight = _options.Bold ? FontWeights.SemiBold : FontWeights.Medium;
        text.TextWrapping = TextWrapping.Wrap;
        text.TextAlignment = _right ? TextAlignment.Right : TextAlignment.Left;
        text.LineHeight = height * _scale * (small ? .75 : 1);
        text.Margin = margin;
        _secondary.Add(text);
    }
    private void BuildStrip(WrappedLine wrapped, double available)
    {
        double side = 20 * _fontSize / 41;
        double minimumHeight = 50 * _fontSize / 41, h = Math.Max(minimumHeight, wrapped.Words.Max(word => word.Size.Height));
        var host = new Grid { Height = h, ClipToBounds = false, HorizontalAlignment = _right ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        var canvas = new Canvas { Width = Math.Max(1, wrapped.Width + side * 2), Height = h, ClipToBounds = false, Margin = new Thickness(-side, 0, -side, 0) };
        // WPF maps the mask to rendered bounds; retain the reference's near-transparent padding anchor.
        canvas.Children.Add(new Border { Width = canvas.Width, Height = h, Background = Brushes.White, Opacity = 1E-5, IsHitTestVisible = false });
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        mask.GradientStops.Add(new GradientStop(Colors.White, 0));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(95, 255, 255, 255), 0));
        var views = new List<WordView>();
        double x = side;
        foreach (var span in wrapped.Words)
        {
            bool shine = _options.WordShine && !_background && Timeline.Emphasis(_line.Words, span.Index, _right, _options.Preset);
            var glyphs = new List<Glyph>();
            var wordCanvas = new Canvas { Width = Math.Max(1, span.Size.Width), Height = h, ClipToBounds = false };
            Canvas.SetLeft(wordCanvas, x);
            canvas.Children.Add(wordCanvas);
            var floats = new List<FloatView>();
            if (shine)
            {
                int start = span.Word.Text.StartsWith(' ') ? 1 : 0, count = span.Word.Text.Trim().Length;
                double advance = 0;
                for (int i = 0; i < span.Word.Text.Length; i++)
                {
                    string c = span.Word.Text[i].ToString();
                    var measured = Measure(c);
                    double step = measured.Width;
                    if (i + 1 < span.Word.Text.Length)
                    {
                        double k = measured.Width + Measure(span.Word.Text[i + 1].ToString()).Width - Measure(span.Word.Text.Substring(i, 2)).Width;
                        if (k > .001)
                            step -= k;
                    }
                    advance += step;
                    var view = Text(c, _fontSize, _options.Bold ? FontWeights.Heavy : FontWeights.Bold);
                    view.MinHeight = minimumHeight;
                    if (_options.AppleMusicLyrics.EnablePlusLighter)
                        view.CacheMode = FractionalLiftCache;
                    var scale = new ScaleTransform(1, 1);
                    var move = new TranslateTransform();
                    var transforms = new TransformGroup();
                    transforms.Children.Add(scale);
                    transforms.Children.Add(move);
                    view.RenderTransform = transforms;
                    view.RenderTransformOrigin = new Point(LineBreaker.GlyphOrigin(span.Word.Text.Length, span.Size.InkWidth, advance, step, measured.Width), .75);
                    Canvas.SetLeft(view, advance - step);
                    Canvas.SetTop(view, (h - measured.Height) / 2);
                    wordCanvas.Children.Add(view);
                    glyphs.Add(new Glyph(view, scale, move, i - start, count, Timeline.IsHan(c[0]) ? HanLiftCache : FractionalLiftCache));
                }
            }
            else
            {
                if (span.Word.Syllables.Count > 0 && string.Concat(span.Word.Syllables.Select(s => s.Text)) == span.Word.Text)
                {
                    string prefix = "";
                    foreach (var syllable in span.Word.Syllables)
                    {
                        AddText(syllable.Text, Measure(prefix).Width, syllable.StartMs, syllable.DurationMs, true);
                        prefix += syllable.Text;
                    }
                }
                else
                    AddText(span.Word.Text, 0, span.Word.StartMs, span.Word.DurationMs, false);
                void AddText(string text, double left, double start, double duration, bool syllable)
                {
                    var view = Text(text, _fontSize, _options.Bold ? FontWeights.Heavy : FontWeights.Bold);
                    view.MinHeight = minimumHeight;
                    var move = new TranslateTransform();
                    view.RenderTransform = move;
                    Canvas.SetLeft(view, left);
                    Canvas.SetTop(view, (h - span.Size.Height) / 2);
                    wordCanvas.Children.Add(view);
                    floats.Add(new(view, move, start, duration, syllable, text.Any(Timeline.IsHan) ? HanLiftCache : FractionalLiftCache));
                }
            }
            views.Add(new WordView(span.Index, span.Word, x - side, span.Size.Width, span.Size.InkWidth, glyphs, wordCanvas, floats));
            x += span.Size.Width;
        }
        host.Children.Add(canvas);
        _glyphHost.Children.Add(host);
        _strips.Add(new Strip(canvas, wrapped.Width, mask, views, wrapped));
    }

    internal (bool UseBackEase, double GapSeconds) OpacityTransition { get; set; } = (false, .2);
    internal double SingOpacityDuration { get; set; } = 700;
    internal async Task ResetCurrentWordAsync()
    {
        if (!_karaoke || !SlotOpen)
            return;
        var reset = _wordReset;
        reset.Blocked = true;
        try
        {
            await Task.Delay(100);
            reset.ClockStartedMs = AnimationClockNow?.Invoke() ?? _lastAnimationTimeMs;
            reset.Sequence++;
            if (_lastUpdate is { } last)
                Update(last.Time, last.RowTime, last.Active, last.Distance, last.Start, last.End, last.Generation, reset.ClockStartedMs);
            await Task.Delay(100);
        }
        finally { reset.Blocked = false; }
    }
    private void ApplyWordReset(double time, double now)
    {
        _resetClockStartedMs = now;
        bool floating = _options.Float && _options.AppleMusicLyrics.KaraokeStyleEnableAnimation && !_options.ReduceMotion;
        foreach (var strip in _strips)
        {
            double from = MaskProgress(strip, time, now);
            StartMask(strip);
            strip.Sweep = new(time, _options.ReduceMotion ? 0 : 300, from, 0, false, []);
            strip.SweepClockStartedMs = now;
            strip.Frozen = false;
            if (!floating)
                continue;
            foreach (var word in strip.Words)
            {
                word.ResetStoppedAtMs = AnimationTime(time, now, word.ClockStartedMs ?? _prepareClockStartedMs, word.DispatchStartedMs ?? _prepareLyricTime);
                foreach (var move in word.Floats)
                {
                    move.ResetFromY = move.Move.Y;
                    move.EarlyFromY = move.HeldY = null;
                }
                foreach (var glyph in word.Glyphs)
                {
                    glyph.ResetFromY = glyph.Move.Y != 0 ? glyph.Move.Y : null;
                    glyph.EarlyFromY = glyph.HeldY = null;
                }
            }
        }
        _wordRun = new();
        _finishClockStartedMs = null;
        _resumeResetWords = true;
    }
    internal void RestoreWordRun(LyricRow previous)
    {
        if (!_line.Words.Select(word => (word.Text, word.StartMs, word.DurationMs)).SequenceEqual(previous._line.Words.Select(word => (word.Text, word.StartMs, word.DurationMs))))
            return;
        _wordRun = previous._wordRun.Snapshot();
        _finishClockStartedMs = previous._finishClockStartedMs;
        _prepareClockStartedMs = previous._prepareClockStartedMs;
        _stoppedClockStartedMs = previous._stoppedClockStartedMs;
        _prepareLyricTime = previous._prepareLyricTime;
        SlotOpen = previous.SlotOpen;
        _lineAnimation = previous._lineAnimation;
        _lineAnimationStartedMs = previous._lineAnimationStartedMs;
        _linePlaying = previous._linePlaying;
        _backgroundAnimation = previous._backgroundAnimation;
        _backgroundFrame = previous._backgroundFrame;
        _backgroundDelayMs = previous._backgroundDelayMs;
        _backgroundBeforeDelay = previous._backgroundBeforeDelay;
        _backgroundRight = previous._backgroundRight;
        _overlapBottom = previous._overlapBottom;
        _wordReset = previous._wordReset;
        _appliedResetSequence = previous._appliedResetSequence;
        _resetClockStartedMs = previous._resetClockStartedMs;
        _resumeResetWords = previous._resumeResetWords;
        double ratio = _fontSize / previous._fontSize;
        var words = previous._strips.SelectMany(strip => strip.Words).ToDictionary(word => word.Index);
        for (int stripIndex = 0; stripIndex < _strips.Count; stripIndex++)
        {
            var strip = _strips[stripIndex];
            if (stripIndex < previous._strips.Count)
            {
                var oldStrip = previous._strips[stripIndex];
                bool sameCount = _strips.Count == previous._strips.Count;
                strip.Progress = oldStrip.Progress;
                strip.FinishFrom = oldStrip.FinishFrom;
                if (sameCount)
                {
                    strip.Sweep = oldStrip.Sweep;
                    strip.SweepClockStartedMs = oldStrip.SweepClockStartedMs;
                    strip.ColorAnimation = oldStrip.ColorAnimation;
                    strip.ColorAnimationStartedMs = oldStrip.ColorAnimationStartedMs;
                }
                strip.Frozen = !sameCount || oldStrip.Frozen;
                if (oldStrip.Canvas.OpacityMask is LinearGradientBrush oldMask)
                {
                    strip.Canvas.OpacityMask = strip.Mask;
                    strip.SavedColors = sameCount ? oldStrip.SavedColors : (oldMask.GradientStops[0].Color, oldMask.GradientStops[1].Color);
                }
            }
            else
                strip.Frozen = true;
            foreach (var word in strip.Words)
            {
                if (!words.TryGetValue(word.Index, out var old))
                    continue;
                word.MotionWord = old.MotionWord;
                word.ClockStartedMs = old.ClockStartedMs;
                word.DispatchStartedMs = old.DispatchStartedMs;
                word.ResetStoppedAtMs = old.ResetStoppedAtMs;
                for (int i = 0; i < Math.Min(word.Floats.Count, old.Floats.Count); i++)
                {
                    var next = word.Floats[i];
                    var previousFloat = old.Floats[i];
                    next.PrepareFromY = previousFloat.PrepareFromY * ratio;
                    next.EarlyFromY = previousFloat.EarlyFromY * ratio;
                    next.HeldY = previousFloat.HeldY * ratio;
                    next.ResetFromY = previousFloat.ResetFromY * ratio;
                    next.Move.Y = previousFloat.Move.Y * ratio;
                }
                for (int i = 0; i < Math.Min(word.Glyphs.Count, old.Glyphs.Count); i++)
                {
                    var next = word.Glyphs[i];
                    var previousGlyph = old.Glyphs[i];
                    next.PrepareFromY = previousGlyph.PrepareFromY * ratio;
                    next.EarlyFromY = previousGlyph.EarlyFromY * ratio;
                    next.HeldY = previousGlyph.HeldY * ratio;
                    next.ResetFromY = previousGlyph.ResetFromY * ratio;
                    next.HeldScale = previousGlyph.HeldScale;
                    next.StartScale = previousGlyph.StartScale;
                    next.Move.Y = previousGlyph.Move.Y * ratio;
                    next.Scale.ScaleX = next.Scale.ScaleY = previousGlyph.Scale.ScaleX;
                }
            }
        }
    }
    private void SetWordLift(TextBlock view, TranslateTransform move, double lift, BitmapCache liftCache, bool enabled, bool active,
        bool keepBitmap = false)
    {
        if (UseFractionalLift || keepBitmap)
        {
            bool moving = UseFractionalLift && enabled && (active || lift != move.Y);
            BitmapCache? cache = moving || (keepBitmap && active) ? liftCache : keepBitmap ? FractionalLiftCache : null;
            if (!ReferenceEquals(view.CacheMode, cache))
                view.CacheMode = cache;
        }
        move.Y = lift;
    }

    private bool ChineseSweep() => _options.AppleMusicLyrics.KaraokeChineseRollingEasing && _line.Text.Any(c => c is >= '\u4e00' and <= '\u9fff') && !_line.Text.Any(c => c is >= '\u3040' and <= '\u30ff');
    public void Update(double time, double rowTime, bool active, int distance, double activationStart, double activationEnd, long generation = 0, double? animationTimeMs = null)
    {
        if (_appliedResetSequence != _wordReset.Sequence)
        {
            _appliedResetSequence = _wordReset.Sequence;
            _applyingWordReset = true;
            try
            {
                Update(time, rowTime, active, distance, activationStart, activationEnd, generation, _wordReset.ClockStartedMs);
                ApplyWordReset(time, _wordReset.ClockStartedMs);
            }
            finally { _applyingWordReset = false; }
        }
        _lastUpdate = (time, rowTime, active, distance, activationStart, activationEnd, generation);
        _lastAnimationTimeMs = animationTimeMs ?? time;
        Frame = UpdateLineAnimation(rowTime, active, activationStart, activationEnd, animationTimeMs);
        Opacity = Frame.RootOpacity;
        _rootScale.ScaleX = _rootScale.ScaleY = Frame.RootScale;
        _contentScale.ScaleX = _contentScale.ScaleY = _background ? 1 : Frame.ContentScale;
        bool wasActive = SlotOpen;
        SlotOpen = active;
        if (!active && wasActive)
            _stoppedClockStartedMs = animationTimeMs;
        double elapsed = active ? Math.Max(0, rowTime - activationStart) : Math.Max(0, rowTime - activationEnd);
        double growth = _options.ReduceMotion ? 1 : Motion.Scroll(elapsed / (Motion.ScrollParameters().Duration * 1000 * _options.AnimationSpeed));
        if (_background)
        {
            double height = NaturalHeight, offset = active ? 0 : height, lift = Math.Min(Math.Max(height * .18, 6 * _scale), 20 * _scale) * (_above ? 1 : -1);
            if (BackgroundOverlap)
            {
                var settings = _options.AppleMusicLyrics;
                if (active && animationTimeMs is { } clock && _backgroundAnimation is { MovementDurationMs: > 0 } plan)
                {
                    double progress = Math.Clamp((clock - _lineAnimationStartedMs - _backgroundDelayMs) / plan.MovementDurationMs, 0, 1);
                    bool back = plan.UseBackEase && settings.BackgroundVocalsEnterAnimationNew;
                    double eased = plan.Ios ? Motion.Scroll(progress, back ? plan.ScrollGap : null) : back ? Motion.LegacyBackScroll(progress) : Motion.LegacyScroll(progress);
                    offset = plan.Height * (1 - eased);
                }
                bool compensate = active && settings.BackgroundVocalsEnterAnimationNew && (!settings.BackgroundVocalsEnterAnimationNewNew || _above);
                _outside.Margin = compensate ? new Thickness(0, offset, 0, -offset) : _outsideMargin;
            }
            Margin = new Thickness(0, -offset, 0, 0);
            double originY = _options.AppleMusicLyrics.BackgroundVocalsEnterAnimationNew ? (offset + height / 1.25) / (offset + height) : .5;
            RenderTransformOrigin = new Point(_backgroundRight || _right && !_options.AppleMusicLyrics.BackgroundVocalsEnterDirectionLeft ? 1 : 0, originY);
            _rootMove.Y = BackgroundOverlap && active ? 0 : animationTimeMs.HasValue ? _backgroundFrame.Y : active ? lift * (1 - growth) : rowTime > activationEnd ? height + lift * growth : 0;
        }
        else if (_backingRows.Any(x => x.Above))
        {
            double shift = animationTimeMs.HasValue ? _backingRows.Where(x => x.Above).Sum(x => x.Row.ParentContentOffsetY) : _backingRows.Where(x => x.Above).Sum(x => x.Row.NaturalHeight) * (active ? -1 : rowTime > activationEnd ? 1 : 0) * (1 - growth);
            foreach (var child in new FrameworkElement[] { _glyphHost, _plainText }.Concat(_secondary))
                child.RenderTransform = new TranslateTransform(0, shift);
        }
        if (_overlapBottom is { } bottom && animationTimeMs is { } mainClock)
        {
            double elapsedOverlap = mainClock - bottom.Start;
            if (elapsedOverlap >= 2 * bottom.Duration)
            {
                _overlapBottom = null;
                Margin = new Thickness(0);
            }
            else
            {
                double progress = Math.Clamp(elapsedOverlap / bottom.Duration, 0, 1);
                double eased = bottom.Ios ? Motion.Scroll(progress) : Motion.LegacyScroll(progress);
                Margin = new Thickness(0, 0, 0, -bottom.Height * eased);
            }
        }
        _content.Opacity = _karaoke ? 1 : Frame.PlayedAlpha;
        foreach (var text in _secondary)
            text.Opacity = Frame.SecondaryAlpha;
        int level = distance < 0 ? Math.Min(-distance + 1, 4) : Math.Min(distance, 4);
        bool hoverRestore = _options.AppleMusicLyrics.DisableBlurAtLyricsLineMouseOver && _feedback?.Hovered == true, hoverChanged = hoverRestore != _hoverBlurRestored;
        _hoverBlurRestored = hoverRestore;
        double radius = _options.Blur && !active && !hoverRestore ? BlurRadii[level] * _scale * _options.BlurLevel : 0;
        if (_background)
            radius = Math.Min(radius, 7);
        if (Math.Abs(radius - _lastBlur) > .01)
        {
            _lastBlur = radius;
            if (_options.ReduceMotion)
            {
                _blur.BeginAnimation(BlurEffect.RadiusProperty, null);
                _blur.Radius = radius;
            }
            else
                _blur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(radius, TimeSpan.FromMilliseconds(hoverChanged ? (hoverRestore ? 100 : 250) : 90)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        }
        if (animationTimeMs.HasValue)
        foreach (var strip in _strips)
            strip.Progress = MaskProgress(strip, time, animationTimeMs);
        bool wasFinishing = _wordRun.Finishing;
        int previousWord = _wordRun.CurrentWord;
        bool reset = false;
        if (animationTimeMs.HasValue && !active)
            _wordRun.Stop();
        else if (!_wordReset.Blocked && !_applyingWordReset)
            reset = _wordRun.Advance(time, _line.Words, NextLineStartMs, _options.AppleMusicLyrics.LineSwitchingAppleIosStyle164, generation, active);
        if (reset && _resumeResetWords)
        {
            _resumeResetWords = false;
            previousWord = -1;
            reset = false;
        }
        if (reset)
        {
            previousWord = -1;
            _finishClockStartedMs = null;
            _prepareClockStartedMs = animationTimeMs;
            _prepareLyricTime = time;
            _stoppedClockStartedMs = null;
            _resetClockStartedMs = null;
            foreach (var strip in _strips)
            {
                strip.Progress = strip.FinishFrom = 0;
                strip.Sweep = null;
                strip.SweepClockStartedMs = null;
                strip.Frozen = false;
                strip.SavedColors = null;
                strip.Canvas.OpacityMask = active ? strip.Mask : null;
                foreach (var view in strip.Words)
                {
                    view.MotionWord = null;
                    view.ClockStartedMs = view.DispatchStartedMs = view.ResetStoppedAtMs = null;
                    foreach (var move in view.Floats)
                    {
                        move.PrepareFromY = animationTimeMs.HasValue ? move.Move.Y : 0;
                        move.EarlyFromY = move.HeldY = move.ResetFromY = null;
                    }
                    foreach (var glyph in view.Glyphs)
                    {
                        glyph.PrepareFromY = animationTimeMs.HasValue ? glyph.Move.Y : 0;
                        glyph.EarlyFromY = glyph.HeldY = glyph.HeldScale = glyph.StartScale = glyph.ResetFromY = null;
                    }
                }
            }
        }
        foreach (var strip in _strips)
            foreach (var view in strip.Words.Where(view => view.Index > previousWord && view.Index <= _wordRun.CurrentWord))
                if (_wordRun.DispatchFor(view.Index) is { } dispatch)
                {
                    StartMask(strip);
                    strip.Sweep = WordSweep.Dispatch(strip.Layout, view.Index, dispatch.StartedMs, strip.Progress, _fontSize / 41, _feather, ChineseSweep(), text => text == " " ? Measure(text).Width : Measure(text).InkWidth);
                    strip.SweepClockStartedMs = animationTimeMs;
                    strip.Frozen = false;
                    view.MotionWord = dispatch.ShineStarted ? new()
                    {
                        Text = view.Word.Text,
                        StartMs = dispatch.StartedMs,
                        DurationMs = dispatch.DurationMs,
                        Syllables = view.Word.Syllables
                    } : null;
                    view.ClockStartedMs = animationTimeMs;
                    view.DispatchStartedMs = dispatch.StartedMs;
                    view.ResetStoppedAtMs = null;
                    foreach (var move in view.Floats)
                        move.ResetFromY = null;
                    foreach (var glyph in view.Glyphs)
                    {
                        glyph.ResetFromY = null;
                        glyph.StartScale = null;
                    }
                }
        if (_wordRun.Finishing && (!wasFinishing || reset))
        {
            _finishClockStartedMs = animationTimeMs;
            foreach (var strip in _strips)
            {
                StartMask(strip);
                strip.Frozen = false;
                strip.FinishFrom = strip.Progress;
                foreach (var view in strip.Words.Where(view => view.Index > _wordRun.CurrentWord))
                {
                    foreach (var move in view.Floats)
                        move.HeldY = move.Move.Y;
                    foreach (var glyph in view.Glyphs)
                    {
                        glyph.HeldY = glyph.Move.Y;
                        glyph.HeldScale = glyph.Scale.ScaleX;
                    }
                }
                if (_wordRun.CurrentWord < 0 || !_options.Float || !_options.AppleMusicLyrics.KaraokeStyleEnableAnimation || _options.ReduceMotion)
                    continue;
                foreach (var view in strip.Words.Where(view => view.Index >= _wordRun.CurrentWord))
                {
                    foreach (var move in view.Floats)
                    if (move.Move.Y > .05)
                        move.EarlyFromY = move.Move.Y;
                    foreach (var glyph in view.Glyphs)
                    if (glyph.Move.Y > .05)
                        glyph.EarlyFromY = glyph.Move.Y;
                }
            }
        }
        foreach (var strip in _strips)
        {
            bool abnormal = _options.AppleMusicLyrics.BuggyApple && _options.AppleMusicLyrics.BuggyAppleOptions.ChineseCharacterAbnormalFloating && string.Concat(strip.Words.Select(word => word.Word.Text)).Trim().All(Timeline.IsCjk);
            double abnormalLift = abnormal && (active || animationTimeMs is { } abnormalNow && _stoppedClockStartedMs is { } stopped && abnormalNow - stopped < Math.Truncate(SingOpacityDuration * _options.AnimationSpeed)) ? _options.AppleMusicLyrics.GetFloatHeight() * _fontSize / 41 : 0;
            strip.Canvas.Margin = new Thickness(strip.Canvas.Margin.Left, -abnormalLift, strip.Canvas.Margin.Right, abnormalLift);
            bool sudden = _options.AppleMusicLyrics.BuggyApple && (_options.AppleMusicLyrics.BuggyAppleOptions.LyricsLineTransparencySuddenChange || _backingRows.Count > 0 && _options.AppleMusicLyrics.BuggyAppleOptions.BackgroundVocalTransparencySuddenChange);
            bool eventExit = animationTimeMs.HasValue && _lineAnimation is { Playing: false };
            double stopElapsed = eventExit ? Math.Max(0, animationTimeMs!.Value - _lineAnimationStartedMs) : rowTime - activationEnd;
            double stopDuration = eventExit ? _lineAnimation!.PlayedDurationMs : sudden ? 10 : Math.Truncate(SingOpacityDuration * _options.AnimationSpeed);
            if (!active && activationEnd > 0 && stopElapsed >= stopDuration)
                strip.Canvas.OpacityMask = null;
            strip.Canvas.Opacity = strip.Canvas.OpacityMask is null ? 47d / 255 : 1;
            var colors = animationTimeMs is { } colorClock && strip.ColorAnimation is { } colorPlan ? colorPlan.At(Math.Max(0, colorClock - strip.ColorAnimationStartedMs)) : Frame;
            strip.Mask.GradientStops[0].Color = strip.SavedColors?.Played ?? Color.FromArgb((byte)Math.Clamp(Math.Round(colors.PlayedAlpha * 255), 0, 255), 255, 255, 255);
            strip.Mask.GradientStops[1].Color = strip.SavedColors?.Unplayed ?? Color.FromArgb((byte)Math.Clamp(Math.Round(colors.UnplayedAlpha * 255), 0, 255), 255, 255, 255);
            double factor = _fontSize / 41;
            bool chinese = ChineseSweep();
            double progress = MaskProgress(strip, time, animationTimeMs);
            strip.Progress = progress;
            var offsets = Motion.Mask(progress, strip.Width, factor, _feather);
            strip.Mask.GradientStops[0].Offset = offsets.First;
            strip.Mask.GradientStops[1].Offset = offsets.Second;
            foreach (var view in strip.Words)
            {
                var selected = view.DispatchStartedMs;
                double lyricOrigin = selected ?? _prepareLyricTime;
                double? clockOrigin = selected.HasValue ? view.ClockStartedMs : _prepareClockStartedMs;
                double effectTime = AnimationTime(time, animationTimeMs, clockOrigin, lyricOrigin);
                bool past = activationEnd > 0 && rowTime > activationEnd;
                bool resetStopping = view.ResetStoppedAtMs.HasValue;
                double stopTime = view.ResetStoppedAtMs ?? AnimationTime(activationEnd - (rowTime - time), _stoppedClockStartedMs, clockOrigin, lyricOrigin);
                double floatHeight = _options.AppleMusicLyrics.GetFloatHeight();
                bool floatingEnabled = _options.Float && _options.AppleMusicLyrics.KaraokeStyleEnableAnimation && !_options.ReduceMotion;
                bool ios = _options.AppleMusicLyrics.InterLineAnimationPreset == InterLineAnimationPresetTypes.iOS;
                bool livePrepare = animationTimeMs.HasValue && clockOrigin.HasValue && _prepareClockStartedMs.HasValue;
                // Sample preparation on the same clock as the dispatched word.
                double prepareStart = livePrepare ? lyricOrigin + _prepareClockStartedMs!.Value - clockOrigin!.Value : activationStart - (rowTime - time);
                double floatSampleTime = past ? Math.Min(effectTime, stopTime) : effectTime;
                bool immediate = _options.AppleMusicLyrics.BuggyApple && _options.AppleMusicLyrics.BuggyAppleOptions.WordShineAnimationInterruption;
                bool wordStarted = view.MotionWord is not null;
                var motionWord = view.MotionWord ?? view.Word;
                bool effectActive = wordStarted && (_options.Preset == MotionPreset.Lyricify || Timeline.Emphasis(motionWord, view.Index == _line.Words.Count - 1, _options.Preset));
                if (view.Glyphs.Count > 0 && _options.Preset != MotionPreset.Android)
                {
                    double opacity = 0;
                    if (!_options.ReduceMotion && effectActive)
                    {
                        if (active && !resetStopping)
                            opacity = Motion.WordGlow(effectTime, motionWord);
                        else if ((past || resetStopping) && !immediate)
                            opacity = _options.Preset == MotionPreset.Lyricify ? Motion.WordGlow(effectTime, motionWord, stopTime) : Motion.WordGlow(stopTime, motionWord) * (1 - Math.Clamp((effectTime - stopTime) / 700, 0, 1));
                    }
                    ApplyGlow(view.Host, view.Host, opacity, 13);
                }
                foreach (var floating in view.Floats)
                {
                    double floatStart = view.DispatchStartedMs is { } dispatched ? dispatched + floating.Start - view.Word.StartMs : floating.Start;
                    double fromY = livePrepare ? floating.PrepareFromY : 0;
                    double value = 0;
                    if (floatingEnabled)
                        value = animationTimeMs.HasValue && selected is null
                            ? FloatingMotion.Prepare(floatSampleTime, prepareStart, _fontSize, floatHeight, _options.AnimationSpeed, ios, fromY)
                            : FloatingMotion.Y(floatSampleTime, floatStart, floating.Duration, prepareStart, _fontSize, floating.Syllable, floatHeight, _options.AnimationSpeed, ios, fromY);
                    if (floatingEnabled && floating.EarlyFromY is { } from)
                        value = EarlyY(from, floatSampleTime);
                    else if (floatingEnabled && floating.HeldY is { } held)
                        value = held;
                    if (resetStopping && floating.ResetFromY is { } resetFrom)
                        value = ResetY(resetFrom);
                    double lift = active ? value : past ? FloatingMotion.LineReturn(effectTime - stopTime, value, _options.AnimationSpeed, ios) : 0;
                    SetWordLift(floating.View, floating.Move, lift, floating.LiftCache, floatingEnabled, active);
                }
                foreach (var glyph in view.Glyphs)
                {
                    (double Scale, double Y, double Glow) state = (1, 0, 0);
                    if (!_options.ReduceMotion)
                    {
                        if (active && wordStarted && !resetStopping)
                            state = Pose(effectTime);
                        else if ((past || resetStopping) && wordStarted && view.Word.StartMs <= stopTime)
                        {
                            if (immediate)
                                state = (1, -(_options.Float ? floatHeight * factor : 0), 0);
                            else if (_options.Preset == MotionPreset.Lyricify)
                                state = Pose(effectTime, stopTime);
                            else
                                state = Motion.StopGlyph(Pose(stopTime), effectTime - stopTime, Math.Min(motionWord.DurationMs / 1000, 3), _options.Preset == MotionPreset.Ios264 ? 1.4695433963885915 : 1.5, _options.Float ? floatHeight * factor : 0, immediate);
                        }
                    }
                    if (!_options.ReduceMotion && !wordStarted)
                    {
                        double scale = past && immediate ? 1 : glyph.HeldScale ?? glyph.Scale.ScaleX;
                        if (active && floatingEnabled)
                            state = (scale, PreparedY(effectTime), 0);
                        if (glyph.EarlyFromY is { } earlyFrom)
                            state = (scale, past && immediate ? -floatHeight * factor : EarlyY(earlyFrom, effectTime), 0);
                        else if (glyph.HeldY is { } held)
                            state = (scale, past && immediate ? -floatHeight * factor : held, 0);
                    }
                    if (resetStopping && glyph.ResetFromY is { } resetFrom)
                        state = (state.Scale, ResetY(resetFrom), state.Glow);
                    if (past && !_options.ReduceMotion)
                    {
                        double from = immediate ? -(_options.Float ? floatHeight * factor : 0) : wordStarted && view.Word.StartMs <= stopTime ? Pose(stopTime).Y : glyph.EarlyFromY is { } early ? EarlyY(early, stopTime) : glyph.HeldY ?? 0;
                        if (resetStopping && glyph.ResetFromY is { } resetFromStop)
                            from = ResetY(resetFromStop);
                        if (from != 0)
                            state = (state.Scale, FloatingMotion.LineReturn(effectTime - stopTime, from, _options.AnimationSpeed, _options.AppleMusicLyrics.InterLineAnimationPreset == InterLineAnimationPresetTypes.iOS), state.Glow);
                    }
                    glyph.Scale.ScaleX = state.Item1;
                    glyph.Scale.ScaleY = state.Item1;
                    SetWordLift(glyph.View, glyph.Move, state.Item2, glyph.LiftCache, floatingEnabled, active,
                        keepBitmap: _options.AppleMusicLyrics.EnablePlusLighter);
                    if (_options.Preset == MotionPreset.Android)
                        ApplyGlow(glyph.View, view.Host, state.Glow, 9);
                    (double Scale, double Y, double Glow) Pose(double at, double stoppedAtMs = double.PositiveInfinity)
                    {
                        double initialY = 0;
                        double initialScale = 1;
                        double growDelay = Motion.GlyphDelay(motionWord, glyph.Index, glyph.Count, _options.Preset);
                        if (floatingEnabled)
                            initialY = PreparedY(effectActive ? Math.Min(at, motionWord.StartMs + growDelay) : at);
                        if (_options.Preset != MotionPreset.Lyricify)
                        {
                            if (at >= motionWord.StartMs + growDelay)
                                glyph.StartScale ??= glyph.Scale.ScaleX;
                            initialScale = glyph.StartScale ?? glyph.Scale.ScaleX;
                        }
                        else
                            initialScale = glyph.StartScale ??= glyph.Scale.ScaleX;
                        if (wordStarted && !effectActive)
                            return (initialScale, initialY, 0);
                        double height = glyph.View.ActualHeight > 0 ? glyph.View.ActualHeight : 50 * factor;
                        var pose = _options.Preset == MotionPreset.Lyricify ? Motion.LegacyGlyph(at, motionWord, glyph.Index, glyph.Count, _fontSize, _options.Float, floatHeight, initialY, initialScale, stoppedAtMs, _options.AppleMusicLyrics.InterLineAnimationPreset == InterLineAnimationPresetTypes.iOS) : Motion.Glyph(at, motionWord, glyph.Index, glyph.Count, _options.Preset, _fontSize, height, _options.Float, floatHeight, initialY, initialScale);
                        return glyph.EarlyFromY is { } from ? (pose.Scale, EarlyY(from, at), pose.Glow) : pose;
                    }
                    double PreparedY(double at) => FloatingMotion.Prepare(at, prepareStart, _fontSize, floatHeight, _options.AnimationSpeed, ios, livePrepare ? glyph.PrepareFromY : 0);
                }
                double EarlyY(double from, double at)
                {
                    double elapsed = at - _wordRun.FinishStartedMs;
                    if (animationTimeMs is { } now && _finishClockStartedMs is { } started)
                        elapsed = Math.Max(0, Math.Min(now, _stoppedClockStartedMs ?? now) - started);
                    return FloatingMotion.EarlyReturn(elapsed, from, _fontSize, floatHeight);
                }
                double ResetY(double from)
                {
                    double now = animationTimeMs ?? time, duration = (_options.AppleMusicLyrics.InterLineAnimationPreset == InterLineAnimationPresetTypes.iOS ? Math.Round(Motion.ScrollParameters().Duration * 1000) : 500) * _options.AnimationSpeed - 50;
                    double fraction = _options.ReduceMotion || duration <= 0 ? 1 : Math.Clamp((Math.Min(now, _stoppedClockStartedMs ?? now) - (_resetClockStartedMs ?? now)) / duration, 0, 1);
                    return from + (floatHeight * factor - from) * fraction;
                }
            }
        }
    }
    private void ApplyGlow(FrameworkElement target, Canvas word, double opacity, double baseRadius)
    {
        if (opacity <= 0)
        {
            if (target.Effect is DropShadowEffect)
                target.Effect = null;
            return;
        }
        if (target.Effect is DropShadowEffect glow)
        {
            glow.Opacity = opacity;
            return;
        }
        // The original measures the word relative to its window, outside the individual glyph scale.
        double scale = _rootScale.ScaleX * _contentScale.ScaleX;
        if (Window.GetWindow(this) is { } window && word.IsDescendantOf(window) && word.TransformToVisual(window) is MatrixTransform transform)
            scale = transform.Matrix.M11;
        if (!double.IsFinite(scale) || scale <= 0)
            scale = 1;
        target.Effect = new DropShadowEffect
        {
            Color = Colors.White,
            ShadowDepth = 0,
            Opacity = opacity,
            BlurRadius = baseRadius * _fontSize / 41 * Math.Pow(1.25 / scale, .36)
        };
    }
    private LineFrame UpdateLineAnimation(double rowTime, bool active, double start, double end, double? animationTimeMs)
    {
        if (animationTimeMs is not { } now)
            return LineMotion.At(rowTime, start, end, _karaoke, _background, _options.Blur, _options.AnimationSpeed, _options.ReduceMotion, _options.AppleMusicLyrics, _backingRows.Count > 0, OpacityTransition.UseBackEase, OpacityTransition.GapSeconds, SingOpacityDuration);
        var idle = LineMotion.At(0, 1, 0, _karaoke, _background, _options.Blur, settings: _options.AppleMusicLyrics);
        if (_background)
            idle = idle with
            {
                RootScale = _options.AppleMusicLyrics.InterLineAnimationPreset == InterLineAnimationPresetTypes.iOS ? .9 : 0,
                ContentScale = 1
            };
        _linePlaying ??= false;
        var current = WithBackgroundRoot(_lineAnimation?.At(Math.Max(0, now - _lineAnimationStartedMs)) ?? idle, now);
        if (_linePlaying == active)
            return current;
        double delay = _options.ReduceMotion ? 0 : Math.Max(0, SwitchDelayMs);
        SwitchDelayMs = 0;
        if (_background)
        {
            var transition = active ? OpacityTransition : BackgroundExitTransition;
            _backgroundBeforeDelay = _backgroundFrame;
            _backgroundDelayMs = delay;
            _backgroundAnimation = BackgroundMotion.Start(current, active, NaturalHeight, _above, _scale, _options.AppleMusicLyrics, _pageKaraoke, transition.UseBackEase, transition.GapSeconds, _options.ReduceMotion);
        }
        else if (active && !_options.ReduceMotion)
        {
            double height = _backingRows.Where(x => x.Row.BackgroundOverlap).Sum(x => x.Row.NaturalHeight);
            if (height > 0)
            {
                var settings = _options.AppleMusicLyrics;
                double duration = Motion.ScrollDurationMs(settings, _pageKaraoke) * _options.AnimationSpeed;
                _overlapBottom = (now + delay, duration, height, settings.InterLineAnimationPreset == InterLineAnimationPresetTypes.iOS);
            }
        }
        _lineAnimation = LineMotion.Start(current, active, _karaoke, _background, _options.Blur, _options.AnimationSpeed, _options.ReduceMotion, _options.AppleMusicLyrics, _backingRows.Count > 0, active && !_background && OpacityTransition.UseBackEase, OpacityTransition.GapSeconds, SingOpacityDuration) with
        {
            ScaleDelayMs = delay
        };
        _lineAnimationStartedMs = now;
        _linePlaying = active;
        foreach (var strip in _strips)
        {
            double played = 47d / 255, unplayed = played;
            if (strip.Canvas.OpacityMask is not null)
            {
                var previous = strip.ColorAnimation?.At(Math.Max(0, now - strip.ColorAnimationStartedMs));
                played = strip.SavedColors?.Played.A / 255d ?? previous?.PlayedAlpha ?? strip.Mask.GradientStops[0].Color.A / 255d;
                unplayed = strip.SavedColors?.Unplayed.A / 255d ?? previous?.UnplayedAlpha ?? strip.Mask.GradientStops[1].Color.A / 255d;
            }
            strip.ColorAnimation = _lineAnimation with
            {
                From = _lineAnimation.From with
                {
                    PlayedAlpha = Math.Round(played * 255) / 255,
                    UnplayedAlpha = Math.Round(unplayed * 255) / 255
                }
            };
            strip.ColorAnimationStartedMs = now;
            strip.SavedColors = null;
        }
        return WithBackgroundRoot(_lineAnimation.At(0), now);
    }
    private LineFrame WithBackgroundRoot(LineFrame frame, double now)
    {
        if (!_background)
            return frame;
        _backgroundFrame = now - _lineAnimationStartedMs < _backgroundDelayMs ? _backgroundBeforeDelay : _backgroundAnimation?.At(Math.Max(0, now - _lineAnimationStartedMs - _backgroundDelayMs)) ?? new BackgroundFrame(frame.RootOpacity, frame.RootScale, 0, 0);
        if (BackgroundOverlap && _linePlaying == true)
            _backgroundFrame = _backgroundFrame with
            {
                Y = 0,
                ParentOffsetY = 0
            };
        return frame with
        {
            RootOpacity = _backgroundFrame.Opacity,
            RootScale = _backgroundFrame.Scale,
            ContentScale = 1
        };
    }
    private static double AnimationTime(double fallback, double? now, double? origin, double lyricOrigin) => now is { } clock && origin is { } started ? lyricOrigin + Math.Max(0, clock - started) : fallback;
    private double MaskProgress(Strip strip, double time, double? animationTimeMs)
    {
        if (strip.Frozen)
            return strip.Progress;
        if (_wordRun.Finishing)
            return _wordRun.FinishProgress(strip.FinishFrom, animationTimeMs is { } now && _finishClockStartedMs is { } origin ? _wordRun.FinishStartedMs + Math.Max(0, now - origin) : time);
        if (strip.Sweep is not { } sweep)
            return strip.Progress;
        return sweep.At(animationTimeMs is { } clock && strip.SweepClockStartedMs is { } started ? sweep.StartedMs + Math.Max(0, clock - started) : time);
    }
    private void StartMask(Strip strip)
    {
        if (strip.Canvas.OpacityMask is null || strip.SavedColors.HasValue)
        {
            strip.ColorAnimation = null;
            strip.SavedColors = (Color.FromArgb(_background ? (byte)159 : (byte)255, 255, 255, 255), Color.FromArgb((byte)_options.AppleMusicLyrics.GetPreplayOpacity(), 255, 255, 255));
        }
        strip.Canvas.OpacityMask = strip.Mask;
    }
}
