using System.Windows;
using System.Windows.Controls;
using BaoToolsGui.ViewModels;

namespace BaoToolsGui.Views;

public partial class DownloadsView : UserControl
{
    public DownloadsView(DownloadsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.ScrollToQueueAction = () =>
        {
            Dispatcher.InvokeAsync(() => QueueSectionHeader?.BringIntoView());
        };

        viewModel.ScrollToHistoryAction = () =>
        {
            Dispatcher.InvokeAsync(() => HistorySectionHeader?.BringIntoView());
        };
    }
}
