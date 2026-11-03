using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Dou_GW_Studio.ViewModels;
namespace Dou_GW_Studio.Views;
public partial class CustomView : UserControl
{
    public CustomView() { InitializeComponent(); }
    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel main) return;
        var rows = TransitionGrid.SelectedItems.Cast<StepViewModel>().ToArray();
        if (rows.Length == 0 && main.Custom.Selected != null) rows = [main.Custom.Selected];
        main.Custom.DeleteSelectedSteps(rows);
    }
}
