using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using WhisperWind.Core;

namespace WhisperWind.App.Controls;

/// <summary>
/// 音流：横向滚动的钢琴卷帘，显示当前位置前后几秒要吹的音。
/// 普通音 = 青绿，升半音 = 赭红，高八度 = 金；正在吹的音 = 印章红。
/// </summary>
public sealed class PianoRoll : FrameworkElement
{
    public static readonly DependencyProperty EventsProperty = Reg<IReadOnlyList<KeyEvent>?>(nameof(Events), null);
    public static readonly DependencyProperty PositionMsProperty = Reg(nameof(PositionMs), 0.0);
    public static readonly DependencyProperty ActiveIndexProperty = Reg(nameof(ActiveIndex), -1);
    public static readonly DependencyProperty WindowMsProperty = Reg(nameof(WindowMs), 9000.0);

    public IReadOnlyList<KeyEvent>? Events { get => (IReadOnlyList<KeyEvent>?)GetValue(EventsProperty); set => SetValue(EventsProperty, value); }
    public double PositionMs { get => (double)GetValue(PositionMsProperty); set => SetValue(PositionMsProperty, value); }
    public int ActiveIndex { get => (int)GetValue(ActiveIndexProperty); set => SetValue(ActiveIndexProperty, value); }
    public double WindowMs { get => (double)GetValue(WindowMsProperty); set => SetValue(WindowMsProperty, value); }

    private static DependencyProperty Reg<T>(string name, T def) => DependencyProperty.Register(
        name, typeof(T), typeof(PianoRoll), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    private const int LowPitch = 59, HighPitch = 85;
    private const double PlayheadRatio = 0.18;

    private static readonly Brush Sage = Freeze(new SolidColorBrush(Color.FromRgb(0x7F, 0x9A, 0x74)));
    private static readonly Brush Terracotta = Freeze(new SolidColorBrush(Color.FromRgb(0xD9, 0x8A, 0x6A)));
    private static readonly Brush Gold = Freeze(new SolidColorBrush(Color.FromRgb(0xC9, 0xA5, 0x52)));
    private static readonly Brush Moss = Freeze(new SolidColorBrush(Color.FromRgb(0x4F, 0x6B, 0x5A)));
    private static readonly Brush Seal = Freeze(new SolidColorBrush(Color.FromRgb(0xC0, 0x46, 0x3A)));
    private static readonly Pen GridPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0xB5, 0xAA, 0x93))), 1)
    {
        DashStyle = Freeze(new DashStyle(new double[] { 2, 4 }, 0)),
    });
    private static readonly Pen PlayheadPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0xAA, 0xC0, 0x46, 0x3A))), 1.5));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 10 || h < 10) return;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        double rowH = h / (HighPitch - LowPitch);
        foreach (var p in new[] { 60, 67, 72, 79, 84 })
        {
            double y = Math.Round(h - (p - LowPitch) * rowH) + 0.5;
            dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
        }

        double playX = w * PlayheadRatio;
        dc.DrawLine(PlayheadPen, new Point(playX, 0), new Point(playX, h));

        var events = Events;
        if (events == null || events.Count == 0)
        {
            dc.Pop();
            return;
        }

        double pos = PositionMs;
        double pxPerMs = w / WindowMs;
        double from = pos - playX / pxPerMs;
        double to = pos + (w - playX) / pxPerMs;

        int i = Math.Max(0, LowerBound(events, (long)from) - 1);
        double barH = Math.Max(4, Math.Min(rowH * 1.4, 9));
        int active = ActiveIndex;
        for (; i < events.Count && events[i].StartMs <= to; i++)
        {
            var e = events[i];
            if (e.EndMs < from) continue;
            double x1 = playX + (e.StartMs - pos) * pxPerMs;
            double x2 = playX + (e.EndMs - pos) * pxPerMs;
            double y = h - (Math.Clamp(e.Pitch, LowPitch, HighPitch) - LowPitch) * rowH - barH / 2;
            var brush = i == active ? Seal
                : e.Keys.Contains(GameKey.OctaveUp) ? Gold
                : e.Keys.Contains(GameKey.OctaveDown) ? Moss
                : e.Keys.Contains(GameKey.Sharp) ? Terracotta
                : Sage;
            bool past = e.EndMs < pos && i != active;
            if (past) dc.PushOpacity(0.35);
            dc.DrawRoundedRectangle(brush, null, new Rect(x1, y, Math.Max(3, x2 - x1), barH), barH / 2, barH / 2);
            if (past) dc.Pop();
        }
        dc.Pop();
    }

    private static int LowerBound(IReadOnlyList<KeyEvent> events, long ms)
    {
        int lo = 0, hi = events.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (events[mid].StartMs < ms) lo = mid + 1; else hi = mid;
        }
        return lo;
    }
}
