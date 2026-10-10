
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VisionInspect.WPF.Models;

namespace VisionInspect.WPF.Controls;

public enum BenchmarkChartType
{
    GroupedBar,
    SpeedBar,
    Radar
}

public sealed class BenchmarkCharts : Canvas
{
    // ==========================================
    // COLORS
    // ==========================================

    private static readonly Brush PatchCoreBrush =
        CreateBrush("#38BDF8");

    private static readonly Brush PaDimBrush =
        CreateBrush("#A78BFA");

    private static readonly Brush EfficientAdBrush =
        CreateBrush("#34D399");

    private static readonly Brush TextBrush =
        CreateBrush("#CBD5E1");

    private static readonly Brush MutedBrush =
        CreateBrush("#94A3B8");

    private static readonly Brush GridBrush =
        CreateBrush("#405065");

    private static readonly Brush TrackBrush =
        CreateBrush("#334155");

    private static readonly Brush HoverBrush =
        CreateBrush("#64748B");

    // ==========================================
    // DEPENDENCY PROPERTIES
    // ==========================================

    public static readonly DependencyProperty ResultsProperty =
        DependencyProperty.Register(
            nameof(Results),
            typeof(IEnumerable<BenchmarkModelResult>),
            typeof(BenchmarkCharts),
            new PropertyMetadata(null, OnChartChanged));

    public IEnumerable<BenchmarkModelResult>? Results
    {
        get => (IEnumerable<BenchmarkModelResult>?)
            GetValue(ResultsProperty);

        set => SetValue(ResultsProperty, value);
    }

    public static readonly DependencyProperty ChartTypeProperty =
        DependencyProperty.Register(
            nameof(ChartType),
            typeof(BenchmarkChartType),
            typeof(BenchmarkCharts),
            new PropertyMetadata(
                BenchmarkChartType.GroupedBar,
                OnChartChanged));

    public BenchmarkChartType ChartType
    {
        get => (BenchmarkChartType)GetValue(ChartTypeProperty);
        set => SetValue(ChartTypeProperty, value);
    }

    // ==========================================
    // FIELDS
    // ==========================================

    private readonly ToolTip _hoverToolTip;

    private string? _currentHoverKey;

    private readonly List<BenchmarkModelResult> _cachedResults = [];

    private static readonly string[] MetricNames =
    {
        "Image AUROC",
        "Image F1",
        "Pixel AUROC",
        "Pixel F1"
    };

    // ==========================================
    // CONSTRUCTOR
    // ==========================================

    public BenchmarkCharts()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;

        _hoverToolTip = new ToolTip
        {
            Background = CreateBrush("#243449"),
            Foreground = Brushes.White,
            BorderBrush = CreateBrush("#52657B"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 9, 12, 9),
            Placement = System.Windows.Controls.Primitives
                .PlacementMode.Mouse,
            StaysOpen = true,
            IsOpen = false
        };

        Loaded += (_, _) => DrawChart();

        Unloaded += (_, _) => CloseHover();

        SizeChanged += (_, _) => DrawChart();

        MouseMove += OnChartMouseMove;

        MouseLeave += (_, _) => CloseHover();
    }

    // ==========================================
    // HELPERS
    // ==========================================

    private static Brush CreateBrush(string hex)
    {
        var brush = (SolidColorBrush)
            new BrushConverter().ConvertFromString(hex)!;

        brush.Freeze();

        return brush;
    }

    private static Brush GetModelBrush(string? modelKey)
    {
        string key = (modelKey ?? "")
            .Replace("_", "")
            .Replace("-", "")
            .Replace(" ", "")
            .ToLowerInvariant();

        return key switch
        {
            "patchcore" => PatchCoreBrush,
            "padim" => PaDimBrush,
            "efficientad" => EfficientAdBrush,
            _ => MutedBrush
        };
    }

    private static string GetModelName(BenchmarkModelResult item)
    {
        if (!string.IsNullOrWhiteSpace(item.ModelName))
            return item.ModelName;

        return item.ModelKey;
    }

    private static bool IsSuccess(BenchmarkModelResult item)
    {
        return string.Equals(
            item.Status,
            "success",
            StringComparison.OrdinalIgnoreCase);
    }

    private static double? GetMetric(
        BenchmarkModelResult item,
        int index)
    {
        return index switch
        {
            0 => item.ImageAUROC,
            1 => item.ImageF1,
            2 => item.PixelAUROC,
            3 => item.PixelF1,
            _ => null
        };
    }

    private static string FormatPercent(double? value)
    {
        if (!value.HasValue ||
            !double.IsFinite(value.Value))
        {
            return "N/A";
        }

        return $"{value.Value * 100:F2}%";
    }

    private static string FormatSeconds(double? milliseconds)
    {
        if (!milliseconds.HasValue ||
            !double.IsFinite(milliseconds.Value))
        {
            return "N/A";
        }

        return $"{milliseconds.Value / 1000.0:F3} s";
    }

    private static double ClampMetric(double? value)
    {
        if (!value.HasValue ||
            !double.IsFinite(value.Value))
        {
            return 0;
        }

        return Math.Clamp(value.Value, 0, 1);
    }

    // ==========================================
    // DRAWING HELPERS
    // ==========================================

    private void AddText(
        string text,
        double x,
        double y,
        double size = 11,
        Brush? brush = null,
        FontWeight? fontWeight = null)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = brush ?? TextBrush,
            FontWeight = fontWeight ?? FontWeights.Normal,
            IsHitTestVisible = false
        };

        Children.Add(label);

        SetLeft(label, x);
        SetTop(label, y);
    }

    private void AddCenteredText(
        string text,
        double centerX,
        double y,
        double size = 11,
        Brush? brush = null)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = brush ?? TextBrush,
            IsHitTestVisible = false
        };

        label.Measure(
            new Size(double.PositiveInfinity,
                     double.PositiveInfinity));

        Children.Add(label);

        SetLeft(
            label,
            centerX - label.DesiredSize.Width / 2);

        SetTop(label, y);
    }

    private void AddLine(
        double x1,
        double y1,
        double x2,
        double y2,
        Brush? brush = null,
        double thickness = 1)
    {
        Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush ?? GridBrush,
            StrokeThickness = thickness,
            IsHitTestVisible = false
        });
    }

    private void AddRectangle(
        double x,
        double y,
        double width,
        double height,
        Brush brush,
        double radius = 3)
    {
        if (width <= 0 ||
            height <= 0 ||
            !double.IsFinite(width) ||
            !double.IsFinite(height))
        {
            return;
        }

        var rectangle = new Rectangle
        {
            Width = width,
            Height = height,
            Fill = brush,
            RadiusX = radius,
            RadiusY = radius,
            IsHitTestVisible = false
        };

        Children.Add(rectangle);

        SetLeft(rectangle, x);
        SetTop(rectangle, y);
    }

    // ==========================================
    // CHART UPDATE
    // ==========================================

    private static void OnChartChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is BenchmarkCharts chart)
            chart.DrawChart();
    }

    private void DrawChart()
    {
        CloseHover();

        Children.Clear();
        _cachedResults.Clear();

        if (Results != null)
        {
            _cachedResults.AddRange(
                Results.Where(IsSuccess));
        }

        if (ActualWidth < 100 || ActualHeight < 80)
            return;

        if (_cachedResults.Count == 0)
        {
            AddCenteredText(
                "No benchmark data",
                ActualWidth / 2,
                ActualHeight / 2 - 8,
                13,
                MutedBrush);

            return;
        }

        switch (ChartType)
        {
            case BenchmarkChartType.GroupedBar:
                DrawGroupedBar();
                break;

            case BenchmarkChartType.SpeedBar:
                DrawSpeedBar();
                break;

            case BenchmarkChartType.Radar:
                DrawRadar();
                break;
        }
    }

    // ==========================================
    // GROUPED BAR CHART
    // ==========================================

    private void DrawGroupedBar()
    {
        double left = 55;
        double right = ActualWidth - 25;
        double top = 18;
        double bottom = ActualHeight - 48;

        double width = right - left;
        double height = bottom - top;

        if (width <= 0 || height <= 0)
            return;

        // Y axis and grid
        for (int i = 0; i <= 4; i++)
        {
            double percent = i * 25;

            double y =
                bottom - height * percent / 100.0;

            AddLine(left, y, right, y);

            AddText(
                $"{percent:F0}%",
                8,
                y - 8,
                10,
                MutedBrush);
        }

        int modelCount = _cachedResults.Count;

        double groupWidth = width / MetricNames.Length;

        for (int metricIndex = 0;
             metricIndex < MetricNames.Length;
             metricIndex++)
        {
            double groupStart =
                left + metricIndex * groupWidth;

            double innerWidth = groupWidth * 0.72;

            double slotWidth =
                innerWidth / Math.Max(1, modelCount);

            double barWidth = slotWidth * 0.75;

            double startX =
                groupStart + (groupWidth - innerWidth) / 2;

            for (int modelIndex = 0;
                 modelIndex < modelCount;
                 modelIndex++)
            {
                var item = _cachedResults[modelIndex];

                double? value =
                    GetMetric(item, metricIndex);

                if (!value.HasValue ||
                    !double.IsFinite(value.Value))
                {
                    continue;
                }

                double normalized = ClampMetric(value);

                double barHeight = height * normalized;

                double x =
                    startX + modelIndex * slotWidth;

                double y = bottom - barHeight;

                AddRectangle(
                    x,
                    y,
                    barWidth,
                    barHeight,
                    GetModelBrush(item.ModelKey));
            }

            AddCenteredText(
                MetricNames[metricIndex],
                groupStart + groupWidth / 2,
                bottom + 14,
                11);
        }
    }

    // ==========================================
    // SPEED BAR CHART
    // ==========================================

    private void DrawSpeedBar()
    {
        var valid = _cachedResults
            .Where(x =>
                x.EvaluationTimeMs.HasValue &&
                double.IsFinite(
                    x.EvaluationTimeMs.Value) &&
                x.EvaluationTimeMs.Value >= 0)
            .ToList();

        if (valid.Count == 0)
        {
            AddCenteredText(
                "No evaluation time data",
                ActualWidth / 2,
                ActualHeight / 2,
                12);

            return;
        }

        double maxSeconds = valid.Max(
            x => x.EvaluationTimeMs!.Value / 1000.0);

        if (maxSeconds <= 0)
            maxSeconds = 1;

        double left = Math.Min(125, ActualWidth * 0.29);
        double rightPadding = 85;

        double availableWidth =
            Math.Max(1, ActualWidth - left - rightPadding);

        double top = 12;

        double rowHeight =
            (ActualHeight - 24) / valid.Count;

        for (int i = 0; i < valid.Count; i++)
        {
            var item = valid[i];

            double seconds =
                item.EvaluationTimeMs!.Value / 1000.0;

            double y =
                top + i * rowHeight;

            double barHeight =
                Math.Min(28, rowHeight * 0.52);

            double barY =
                y + (rowHeight - barHeight) / 2;

            AddText(
                GetModelName(item),
                5,
                barY + (barHeight - 13) / 2,
                11);

            AddRectangle(
                left,
                barY,
                availableWidth,
                barHeight,
                TrackBrush);

            double barWidth =
                availableWidth * seconds / maxSeconds;

            AddRectangle(
                left,
                barY,
                barWidth,
                barHeight,
                GetModelBrush(item.ModelKey));

            AddText(
                $"{seconds:F2} s",
                left + availableWidth + 8,
                barY + (barHeight - 13) / 2,
                11,
                TextBrush);
        }
    }

    // ==========================================
    // RADAR CHART
    // ==========================================

    private void DrawRadar()
    {
        double centerX = ActualWidth / 2;
        double centerY = ActualHeight / 2;

        double radius = Math.Min(
            ActualWidth * 0.30,
            ActualHeight * 0.34);

        if (radius <= 0)
            return;

        Point GetPoint(int index, double value)
        {
            double angle =
                -Math.PI / 2 +
                index * Math.PI / 2;

            return new Point(
                centerX +
                Math.Cos(angle) * radius * value,

                centerY +
                Math.Sin(angle) * radius * value);
        }

        // Background levels
        for (int level = 1; level <= 4; level++)
        {
            var polygon = new Polygon
            {
                Stroke = GridBrush,
                StrokeThickness = 1,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };

            for (int i = 0; i < 4; i++)
            {
                polygon.Points.Add(
                    GetPoint(i, level / 4.0));
            }

            Children.Add(polygon);
        }

        // Axes and labels
        for (int i = 0; i < 4; i++)
        {
            var point = GetPoint(i, 1);

            AddLine(
                centerX,
                centerY,
                point.X,
                point.Y);

            var labelPoint =
                GetPoint(i, 1.20);

            AddCenteredText(
                MetricNames[i],
                labelPoint.X,
                labelPoint.Y - 7,
                10);
        }

        // Model polygons
        foreach (var item in _cachedResults)
        {
            Brush strokeBrush =
                GetModelBrush(item.ModelKey);

            var fillBrush =
                strokeBrush.Clone();

            fillBrush.Opacity = 0.12;

            var polygon = new Polygon
            {
                Stroke = strokeBrush,
                StrokeThickness = 2,
                Fill = fillBrush,
                IsHitTestVisible = false
            };

            for (int i = 0; i < 4; i++)
            {
                polygon.Points.Add(
                    GetPoint(
                        i,
                        ClampMetric(GetMetric(item, i))));
            }

            Children.Add(polygon);

            // Visible vertices
            foreach (Point point in polygon.Points)
            {
                var marker = new Ellipse
                {
                    Width = 5,
                    Height = 5,
                    Fill = strokeBrush,
                    IsHitTestVisible = false
                };

                Children.Add(marker);

                SetLeft(marker, point.X - 2.5);
                SetTop(marker, point.Y - 2.5);
            }
        }
    }

    // ==========================================
    // HOVER / TOOLTIP
    // ==========================================

    private void OnChartMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (_cachedResults.Count == 0)
        {
            CloseHover();
            return;
        }

        Point position = e.GetPosition(this);

        switch (ChartType)
        {
            case BenchmarkChartType.GroupedBar:
                HandleGroupedBarHover(position);
                break;

            case BenchmarkChartType.SpeedBar:
                HandleSpeedBarHover(position);
                break;

            case BenchmarkChartType.Radar:
                HandleRadarHover(position);
                break;
        }
    }

    private void HandleGroupedBarHover(Point position)
    {
        double left = 55;
        double right = ActualWidth - 25;
        double top = 18;
        double bottom = ActualHeight - 48;

        if (position.X < left ||
            position.X > right ||
            position.Y < top ||
            position.Y > bottom)
        {
            CloseHover();
            return;
        }

        double groupWidth =
            (right - left) / MetricNames.Length;

        int metricIndex = Math.Clamp(
            (int)((position.X - left) / groupWidth),
            0,
            MetricNames.Length - 1);

        string key = $"metric-{metricIndex}";

        if (_currentHoverKey == key)
            return;

        var rows = _cachedResults.Select(item =>
            (
                Name: GetModelName(item),
                Value: FormatPercent(
                    GetMetric(item, metricIndex)),
                Color: GetModelBrush(item.ModelKey)
            )).ToList();

        ShowHover(
            key,
            MetricNames[metricIndex],
            rows);
    }

    private void HandleSpeedBarHover(Point position)
    {
        var valid = _cachedResults
            .Where(x =>
                x.EvaluationTimeMs.HasValue &&
                double.IsFinite(
                    x.EvaluationTimeMs.Value) &&
                x.EvaluationTimeMs.Value >= 0)
            .ToList();

        if (valid.Count == 0)
        {
            CloseHover();
            return;
        }

        double top = 12;
        double rowHeight =
            (ActualHeight - 24) / valid.Count;

        if (position.Y < top ||
            position.Y > ActualHeight - 12)
        {
            CloseHover();
            return;
        }

        int index = Math.Clamp(
            (int)((position.Y - top) / rowHeight),
            0,
            valid.Count - 1);

        var item = valid[index];

        string key = $"speed-{item.ModelKey}";

        if (_currentHoverKey == key)
            return;

        ShowHover(
            key,
            GetModelName(item),
            new[]
            {
                (
                    Name: "Evaluation Time",
                    Value: FormatSeconds(
                        item.EvaluationTimeMs),
                    Color: GetModelBrush(item.ModelKey)
                )
            });
    }

    private void HandleRadarHover(Point position)
    {
        double centerX = ActualWidth / 2;
        double centerY = ActualHeight / 2;

        double radius = Math.Min(
            ActualWidth * 0.30,
            ActualHeight * 0.34);

        double dx = position.X - centerX;
        double dy = position.Y - centerY;

        double distance =
            Math.Sqrt(dx * dx + dy * dy);

        if (distance > radius * 1.15)
        {
            CloseHover();
            return;
        }

        // Radar는 다각형이 겹치므로
        // 전체 모델의 지표를 함께 표시한다.

        string key = "radar-all";

        if (_currentHoverKey == key)
            return;

        var rows = _cachedResults.Select(item =>
            (
                Name: GetModelName(item),
                Value:
                    $"Image AUROC {FormatPercent(item.ImageAUROC)}\n" +
                    $"Image F1 {FormatPercent(item.ImageF1)}\n" +
                    $"Pixel AUROC {FormatPercent(item.PixelAUROC)}\n" +
                    $"Pixel F1 {FormatPercent(item.PixelF1)}",
                Color: GetModelBrush(item.ModelKey)
            )).ToList();

        ShowHover(
            key,
            "MODEL STRENGTHS",
            rows);
    }

    private void ShowHover(
        string key,
        string title,
        IEnumerable<(string Name, string Value, Brush Color)> rows)
    {
        _currentHoverKey = key;

        var content = new StackPanel
        {
            MinWidth = 190
        };

        content.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (var row in rows)
        {
            var rowPanel = new StackPanel
            {
                Margin = new Thickness(0, 3, 0, 3)
            };

            rowPanel.Children.Add(new TextBlock
            {
                Text = row.Name,
                Foreground = row.Color,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12
            });

            rowPanel.Children.Add(new TextBlock
            {
                Text = row.Value,
                Foreground = TextBrush,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0)
            });

            content.Children.Add(rowPanel);
        }

        _hoverToolTip.Content = content;

        if (!_hoverToolTip.IsOpen)
            _hoverToolTip.IsOpen = true;
    }

    private void CloseHover()
    {
        _currentHoverKey = null;

        if (_hoverToolTip != null)
            _hoverToolTip.IsOpen = false;
    }
}
