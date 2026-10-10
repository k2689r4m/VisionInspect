
using System.Windows;
using VisionInspect.WPF.ViewModels;

namespace VisionInspect.WPF.Views;

public partial class ComparisonWindow : Window
{
    public ComparisonWindow()
    {
        InitializeComponent();

        DataContext = new ComparisonViewModel();
    }

    // 모든 이미지 화면 맞춤
    private void FitAll_Click(
        object sender,
        RoutedEventArgs e)
    {
        PatchCoreViewer.FitImage();
        PaDimViewer.FitImage();
        EfficientADViewer.FitImage();
    }

    // 모든 이미지 100% 크기
    private void ActualSizeAll_Click(
        object sender,
        RoutedEventArgs e)
    {
        PatchCoreViewer.ActualSize();
        PaDimViewer.ActualSize();
        EfficientADViewer.ActualSize();
    }

    // Benchmark 대시보드 열기
    private void Benchmark_Click(
    object sender,
    RoutedEventArgs e)
    {
        try
        {
            var window = new BenchmarkWindow
            {
                Owner = this
            };

            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Benchmark 창 실행 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }


    private void OpenCv_Click(
        object sender,
        RoutedEventArgs e)
    {
        var window = new OpenCvInspectionWindow
        {
            Owner = this
        };

        window.ShowDialog();
    }
}
