
using System;
using System.Windows;
using VisionInspect.WPF.ViewModels;

namespace VisionInspect.WPF.Views;

public partial class BenchmarkWindow : Window
{
    private readonly BenchmarkViewModel _viewModel = new();

    public BenchmarkWindow()
    {
        InitializeComponent();

        DataContext = _viewModel;

        Loaded += BenchmarkWindow_Loaded;
    }

    private async void BenchmarkWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= BenchmarkWindow_Loaded;

        try
        {
            await _viewModel.RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Benchmark 로드 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void Back_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}
