using System.Windows;
using VisionInspect.WPF.ViewModels;

namespace VisionInspect.WPF.Views;

public partial class OpenCvInspectionWindow : Window
{
    public OpenCvInspectionWindow()
    {
        InitializeComponent();

        DataContext = new OpenCvInspectionViewModel();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}