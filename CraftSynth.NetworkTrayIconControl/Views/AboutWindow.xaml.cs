using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace CraftSynth.NetworkTrayIconControl.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            // Set application name
            txtAppName.Text = "CraftSynth.NetworkTrayIconControl";

            // Try to get a reasonable version string
            string version = "1.0";
            try
            {
                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var ver = asm.GetName().Version;
                if (ver != null)
                    version = ver.ToString();
            }
            catch
            {
                // ignore
            }

            txtVersion.Text = $"Version {version}";
            txtDescription.Text = "Monitor and control network devices from your system tray.";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
