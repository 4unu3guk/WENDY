using System.Windows;
using WENDY.Wendy;

namespace WENDY;

public partial class MainWindow : Window
{
    public WendyCore Core { get; }

    public MainWindow(WendyCore core)
    {
        ArgumentNullException.ThrowIfNull(core);

        Core = core;
        InitializeComponent();
    }
}
