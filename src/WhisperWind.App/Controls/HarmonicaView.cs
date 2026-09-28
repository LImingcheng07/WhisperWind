using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WhisperWind.App.Controls;

/// <summary>
/// 口琴：8 个孔对应 8 个主键，演奏时当前孔亮起。修饰键按游戏的鼠标布局指示：
/// 左侧"降"（左键 低八度）、右侧"升"（右键 高八度），半音（中键）在亮起的孔上标 ♯。
/// 点击孔会触发 HoleClicked（用来试音）。纯 OnRender 绘制，尺寸自适应。
/// </summary>
public sealed class HarmonicaView : FrameworkElement
{
    private static readonly string[] Degrees = { "1", "2", "3", "4", "5", "6", "7", "i" };

    public static readonly DependencyProperty ActiveHoleProperty = Reg(nameof(ActiveHole), -1);
    public static readonly DependencyProperty SharpOnProperty = Reg(nameof(SharpOn), false);
    public static readonly DependencyProperty OctaveOnProperty = Reg(nameof(OctaveOn), false);
    public static readonly DependencyProperty OctaveDownOnProperty = Reg(nameof(OctaveDownOn), false);
    public static readonly DependencyProperty KeyLabelsProperty = Reg<IReadOnlyList<string>?>(nameof(KeyLabels), null);
    public static readonly DependencyProperty ShowLabelsProperty = Reg(nameof(ShowLabels), true);

    public int ActiveHole { get => (int)GetValue(ActiveHoleProperty); set => SetValue(ActiveHoleProperty, value); }
    public bool SharpOn { get => (bool)GetValue(SharpOnProperty); set => SetValue(SharpOnProperty, value); }
    public bool OctaveOn { get => (bool)GetValue(OctaveOnProperty); set => SetValue(OctaveOnProperty, value); }
    public bool OctaveDownOn { get => (bool)GetValue(OctaveDownOnProperty); set => SetValue(OctaveDownOnProperty, value); }
    public IReadOnlyList<string>? KeyLabels { get => (IReadOnlyList<string>?)GetValue(KeyLabelsProperty); set => SetValue(KeyLabelsProperty, value); }
    public bool ShowLabels { get => (bool)GetValue(ShowLabelsProperty); set => SetValue(ShowLabelsProperty, value); }

    /// <summary>点击了第几个孔（0..7）</summary>
    public event Action<int>? HoleClicked;

    private static DependencyProperty Reg<T>(string name, T def) => DependencyProperty.Register(
        name, typeof(T), typeof(HarmonicaView), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush HoleBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x2E, 0x22)));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x7F, 0x6A)));
    private static readonly Brush InkBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x2A, 0x26)));
    private static readonly Brush SealBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC0, 0x46, 0x3A)));
    private static readonly Brush GoldBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xB8, 0x94, 0x3F)));
    private static readonly Brush SageBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x5E, 0x7A, 0x55)));
    private static readonly Brush IdlePill = Freeze(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)));
    private static readonly Pen PillPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xD9, 0xCC, 0xB0))), 1));
    private static readonly Pen BodyPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x9C, 0x7A, 0x33))), 1.2));
    private static readonly Brush BodyBrush = Freeze(new LinearGradientBrush(new GradientStopCollection
    {
        new(Color.FromRgb(0xEE, 0xD4, 0x94), 0),
        new(Color.FromRgb(0xD6, 0xB0, 0x62), 0.55),
        new(Color.FromRgb(0xB8, 0x94, 0x3F), 1),
    }, 90));
    private static readonly Brush GlowBrush = Freeze(new RadialGradientBrush(new GradientStopCollection
    {
        new(Color.FromArgb(0xCC, 0xFF, 0xB0, 0x70), 0),
        new(Color.FromArgb(0x00, 0xFF, 0xB0, 0x70), 1),
    }));
    private static readonly Brush LitBrush = Freeze(new LinearGradientBrush(
        Color.FromRgb(0xFF, 0xC0, 0x86), Color.FromRgb(0xE0, 0x6E, 0x3E), 90));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private Rect _body;

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) ? 460 : available.Width;
        double h = double.IsInfinity(available.Height) ? w * 0.26 : available.Height;
        return new Size(w, h);
    }

    private Rect HoleRect(int i)
    {
        double pad = _body.Width * 0.05;
        double slot = (_body.Width - pad * 2) / 8;
        double hw = slot * 0.56;
        double hh = _body.Height * 0.52;
        return new Rect(_body.X + pad + slot * i + (slot - hw) / 2, _body.Y + (_body.Height - hh) / 2, hw, hh);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 40 || h < 20) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // 两侧留给"降""升"指示
        double side = Math.Min(w * 0.12, 64);
        double labelH = ShowLabels ? Math.Min(h * 0.2, 20) : 0;
        _body = new Rect(side, labelH, w - side * 2, h - labelH * 2);

        // 阴影 + 琴身
        var shadow = _body;
        shadow.Offset(0, 4);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x22, 0x5A, 0x44, 0x20)), null, shadow, 10, 10);
        dc.DrawRoundedRectangle(BodyBrush, BodyPen, _body, 10, 10);
        // 两端铆钉线
        var line = new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0x6B, 0x52, 0x20)), 1);
        dc.DrawLine(line, new Point(_body.X + _body.Width * 0.035, _body.Y + 6), new Point(_body.X + _body.Width * 0.035, _body.Bottom - 6));
        dc.DrawLine(line, new Point(_body.Right - _body.Width * 0.035, _body.Y + 6), new Point(_body.Right - _body.Width * 0.035, _body.Bottom - 6));

        double fontSize = Math.Max(9, Math.Min(labelH * 0.72, 13));
        var labels = KeyLabels;
        for (int i = 0; i < 8; i++)
        {
            var r = HoleRect(i);
            bool lit = i == ActiveHole;
            if (lit)
            {
                var glow = r;
                glow.Inflate(r.Width * 0.9, r.Height * 0.5);
                dc.DrawEllipse(GlowBrush, null, new Point(glow.X + glow.Width / 2, glow.Y + glow.Height / 2), glow.Width / 2, glow.Height / 2);
            }
            dc.DrawRoundedRectangle(lit ? LitBrush : HoleBrush, null, r, r.Width / 2, r.Width / 2);
            if (lit && SharpOn)
            {
                double rad = Math.Max(6, Math.Min(r.Width * 0.45, 9));
                var c = new Point(r.Right, r.Y);
                dc.DrawEllipse(SealBrush, null, c, rad, rad);
                var sharp = Text("♯", rad * 1.4, Brushes.White, dpi, serif: false);
                dc.DrawText(sharp, new Point(c.X - sharp.Width / 2, c.Y - sharp.Height / 2));
            }

            if (ShowLabels)
            {
                var deg = Text(Degrees[i], fontSize, lit ? SealBrush : LabelBrush, dpi, serif: true);
                dc.DrawText(deg, new Point(r.X + r.Width / 2 - deg.Width / 2, (labelH - deg.Height) / 2));
                if (labels != null && i < labels.Count)
                {
                    var key = Text(labels[i], fontSize, lit ? SealBrush : InkBrush, dpi, serif: false);
                    dc.DrawText(key, new Point(r.X + r.Width / 2 - key.Width / 2, _body.Bottom + (labelH - key.Height) / 2));
                }
            }
        }

        // 降 / 升 指示（对应游戏：左键降调、右键升调）
        DrawPill(dc, new Rect(4, _body.Y + _body.Height / 2 - 14, side - 12, 28), "降 8ᵛᵇ", OctaveDownOn, SageBrush, dpi);
        DrawPill(dc, new Rect(w - side + 8, _body.Y + _body.Height / 2 - 14, side - 12, 28), "升 8ᵛᵃ", OctaveOn, GoldBrush, dpi);
    }

    private static void DrawPill(DrawingContext dc, Rect r, string text, bool on, Brush onBrush, double dpi)
    {
        if (r.Width < 16) return;
        dc.DrawRoundedRectangle(on ? onBrush : IdlePill, on ? null : PillPen, r, r.Height / 2, r.Height / 2);
        var t = Text(text, Math.Min(12, r.Width / 3.2), on ? Brushes.White : LabelBrush, dpi, serif: true);
        dc.DrawText(t, new Point(r.X + (r.Width - t.Width) / 2, r.Y + (r.Height - t.Height) / 2));
    }

    private static FormattedText Text(string s, double size, Brush b, double dpi, bool serif) => new(
        s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(new FontFamily(serif ? "Noto Serif SC, Source Han Serif SC, SimSun" : "Microsoft YaHei UI"),
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
        size, b, dpi);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var p = e.GetPosition(this);
        for (int i = 0; i < 8; i++)
        {
            var r = HoleRect(i);
            r.Inflate(r.Width * 0.4, r.Height * 0.3);
            if (r.Contains(p))
            {
                HoleClicked?.Invoke(i);
                e.Handled = true;
                return;
            }
        }
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters p) => new PointHitTestResult(this, p.HitPoint);
}
