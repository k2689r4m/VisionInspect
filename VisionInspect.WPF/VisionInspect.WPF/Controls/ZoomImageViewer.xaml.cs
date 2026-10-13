using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VisionInspect.WPF.Controls;

public partial class ZoomImageViewer : UserControl
{
    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(nameof(ImageSource), typeof(ImageSource),
            typeof(ZoomImageViewer), new PropertyMetadata(null, OnImageSourceChanged));

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    private const double MinZoom = 1.0;
    private const double MaxZoom = 20.0;
    private bool _dragging;
    private Point _lastPoint;

    public ZoomImageViewer()
    {
        InitializeComponent();
        Unloaded += (_, _) => EndDrag();
    }

    private static void OnImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ZoomImageViewer viewer && viewer.IsInitialized)
            viewer.ResetView();
    }

    private void ResetView()
    {
        EndDrag();
        ZoomTransform.ScaleX = ZoomTransform.ScaleY = 1.0;
        PanTransform.X = PanTransform.Y = 0;
        ZoomLabel.Text = "FIT";
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => ResetView();

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ImageSource is null) return;
        double oldZoom = ZoomTransform.ScaleX;
        double factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
        double newZoom = Math.Clamp(oldZoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.00001) return;

        // Keep the image point under the mouse fixed during zoom.
        Point mouse = e.GetPosition(Viewport);
        double ratio = newZoom / oldZoom;
        PanTransform.X = mouse.X - (mouse.X - PanTransform.X) * ratio;
        PanTransform.Y = mouse.Y - (mouse.Y - PanTransform.Y) * ratio;
        ZoomTransform.ScaleX = ZoomTransform.ScaleY = newZoom;
        if (newZoom <= MinZoom) { PanTransform.X = 0; PanTransform.Y = 0; }
        ZoomLabel.Text = newZoom <= MinZoom ? "FIT" : $"{newZoom * 100:0}%";
        e.Handled = true;
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ResetView();
            e.Handled = true;
            return;
        }
        if (ImageSource is null || ZoomTransform.ScaleX <= MinZoom) return;
        _lastPoint = e.GetPosition(Viewport);
        _dragging = Viewport.CaptureMouse();
        if (_dragging) { Viewport.Cursor = Cursors.SizeAll; e.Handled = true; }
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
        Point current = e.GetPosition(Viewport);
        PanTransform.X += current.X - _lastPoint.X;
        PanTransform.Y += current.Y - _lastPoint.Y;
        _lastPoint = current;
        e.Handled = true;
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndDrag();
    private void Viewport_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_dragging && e.LeftButton != MouseButtonState.Pressed) EndDrag();
    }

    private void EndDrag()
    {
        _dragging = false;
        if (Viewport.IsMouseCaptured) Viewport.ReleaseMouseCapture();
        Viewport.Cursor = Cursors.Arrow;
    }
}
