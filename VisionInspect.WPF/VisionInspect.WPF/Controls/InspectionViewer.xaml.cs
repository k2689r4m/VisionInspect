
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VisionInspect.WPF.Models;

namespace VisionInspect.WPF.Controls;

public partial class InspectionViewer : UserControl
{
    private readonly MatrixTransform _transform = new();

    private Point _dragStart;
    private bool _dragging;
    private bool _fitMode = true;

    private double _imageWidth = 1;
    private double _imageHeight = 1;

    private const double MinZoom = 0.05;
    private const double MaxZoom = 20.0;

    public InspectionViewer()
    {
        InitializeComponent();

        ImageCanvas.RenderTransform = _transform;
    }

    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(
            nameof(ImageSource),
            typeof(ImageSource),
            typeof(InspectionViewer),
            new PropertyMetadata(null, OnImageChanged));

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public static readonly DependencyProperty DefectsProperty =
        DependencyProperty.Register(
            nameof(Defects),
            typeof(IEnumerable<DefectInfo>),
            typeof(InspectionViewer),
            new PropertyMetadata(null, OnDefectsChanged));

    public IEnumerable<DefectInfo>? Defects
    {
        get => (IEnumerable<DefectInfo>?)GetValue(DefectsProperty);
        set => SetValue(DefectsProperty, value);
    }

    public static readonly DependencyProperty ShowDefectsProperty =
        DependencyProperty.Register(
            nameof(ShowDefects),
            typeof(bool),
            typeof(InspectionViewer),
            new PropertyMetadata(true, OnDefectsChanged));

    public bool ShowDefects
    {
        get => (bool)GetValue(ShowDefectsProperty);
        set => SetValue(ShowDefectsProperty, value);
    }

    private static void OnImageChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        var viewer = (InspectionViewer)d;
        viewer.UpdateImage();
    }

    private static void OnDefectsChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        ((InspectionViewer)d).DrawDefects();
    }

    private void UpdateImage()
    {
        InspectionImage.Source = ImageSource;

        if (ImageSource is null)
        {
            DefectCanvas.Children.Clear();
            return;
        }

        _imageWidth = ImageSource.Width;
        _imageHeight = ImageSource.Height;

        ImageCanvas.Width = _imageWidth;
        ImageCanvas.Height = _imageHeight;

        InspectionImage.Width = _imageWidth;
        InspectionImage.Height = _imageHeight;

        DefectCanvas.Width = _imageWidth;
        DefectCanvas.Height = _imageHeight;

        DrawDefects();

        if (_fitMode)
            FitImage();
    }

    private void DrawDefects()
    {
        DefectCanvas.Children.Clear();

        if (!ShowDefects || Defects is null ||
            ImageSource is null)
            return;

        int index = 1;

        foreach (var defect in Defects)
        {
            var rectangle = new Rectangle
            {
                Width = defect.Width,
                Height = defect.Height,
                Stroke = Brushes.OrangeRed,
                StrokeThickness = 2,
                Fill = Brushes.Transparent
            };

            Canvas.SetLeft(rectangle, defect.X);
            Canvas.SetTop(rectangle, defect.Y);

            DefectCanvas.Children.Add(rectangle);

            var label = new TextBlock
            {
                Text = $"#{index}",
                Foreground = Brushes.White,
                Background = Brushes.OrangeRed,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(4, 2, 4, 2)
            };

            Canvas.SetLeft(label, defect.X);
            Canvas.SetTop(
                label,
                Math.Max(0, defect.Y - 24));

            DefectCanvas.Children.Add(label);

            index++;
        }
    }

    public void FitImage()
    {
        if (ImageSource is null)
            return;

        double viewportWidth = Viewport.ActualWidth;
        double viewportHeight = Viewport.ActualHeight;

        if (viewportWidth <= 0 || viewportHeight <= 0)
            return;

        double scale = Math.Min(
            viewportWidth / _imageWidth,
            viewportHeight / _imageHeight);

        double offsetX =
            (viewportWidth - _imageWidth * scale) / 2;

        double offsetY =
            (viewportHeight - _imageHeight * scale) / 2;

        _transform.Matrix = new Matrix(
            scale, 0,
            0, scale,
            offsetX, offsetY);

        _fitMode = true;

        UpdateZoomText();
    }

    public void ActualSize()
    {
        if (ImageSource is null)
            return;

        double offsetX =
            (Viewport.ActualWidth - _imageWidth) / 2;

        double offsetY =
            (Viewport.ActualHeight - _imageHeight) / 2;

        _transform.Matrix = new Matrix(
            1, 0,
            0, 1,
            offsetX, offsetY);

        _fitMode = false;

        UpdateZoomText();
    }

    public void FocusDefect(DefectInfo defect)
    {
        if (ImageSource is null)
            return;

        double centerX = defect.X + defect.Width / 2.0;
        double centerY = defect.Y + defect.Height / 2.0;

        double scale = Math.Clamp(
            Math.Min(
                Viewport.ActualWidth /
                    Math.Max(defect.Width * 3, 1),
                Viewport.ActualHeight /
                    Math.Max(defect.Height * 3, 1)),
            MinZoom,
            MaxZoom);

        double offsetX =
            Viewport.ActualWidth / 2 - centerX * scale;

        double offsetY =
            Viewport.ActualHeight / 2 - centerY * scale;

        _transform.Matrix = new Matrix(
            scale, 0,
            0, scale,
            offsetX, offsetY);

        _fitMode = false;

        UpdateZoomText();
    }

    private void Viewport_MouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (ImageSource is null)
            return;

        var matrix = _transform.Matrix;
        var position = e.GetPosition(Viewport);

        double factor = e.Delta > 0 ? 1.2 : 1 / 1.2;

        double targetScale = matrix.M11 * factor;

        if (targetScale < MinZoom || targetScale > MaxZoom)
            return;

        matrix.ScaleAt(factor, factor,
            (position.X - matrix.OffsetX) / matrix.M11,
            (position.Y - matrix.OffsetY) / matrix.M22);

        // 화면 좌표 기준으로 확대 중심을 유지
        matrix.OffsetX =
            position.X -
            (position.X - _transform.Matrix.OffsetX) *
            factor;

        matrix.OffsetY =
            position.Y -
            (position.Y - _transform.Matrix.OffsetY) *
            factor;

        _transform.Matrix = matrix;
        _fitMode = false;

        UpdateZoomText();
        e.Handled = true;
    }

    private void Viewport_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (ImageSource is null)
            return;

        _dragStart = e.GetPosition(Viewport);
        _dragging = true;
        Viewport.CaptureMouse();
    }

    private void Viewport_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        _dragging = false;
        Viewport.ReleaseMouseCapture();
    }

    private void Viewport_LostMouseCapture(
        object sender,
        MouseEventArgs e)
    {
        _dragging = false;
    }

    private void Viewport_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (ImageSource is null)
            return;

        var position = e.GetPosition(Viewport);

        if (_dragging)
        {
            var matrix = _transform.Matrix;

            matrix.Translate(
                position.X - _dragStart.X,
                position.Y - _dragStart.Y);

            _transform.Matrix = matrix;
            _dragStart = position;
        }

        var inverse = _transform.Matrix;

        if (!inverse.HasInverse)
            return;

        inverse.Invert();

        Point imagePoint = inverse.Transform(position);

        if (imagePoint.X >= 0 &&
            imagePoint.Y >= 0 &&
            imagePoint.X < _imageWidth &&
            imagePoint.Y < _imageHeight)
        {
            CoordinateText.Text =
                $"X: {(int)imagePoint.X}  Y: {(int)imagePoint.Y}";
        }
        else
        {
            CoordinateText.Text = "X: -  Y: -";
        }
    }

    private void Viewport_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        if (_fitMode)
            FitImage();
    }

    private void UpdateZoomText()
    {
        ZoomText.Text =
            $"{_transform.Matrix.M11 * 100:F0}%";
    }
}
