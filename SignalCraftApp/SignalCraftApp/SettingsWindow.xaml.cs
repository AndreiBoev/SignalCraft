using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace SignalCraftApp
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            TBArduinoPath.Text = Properties.Settings.Default.ArduinoPath;
            TBQuartusPath.Text = Properties.Settings.Default.QuartusPath;
        }

        private void BtnBrowseArduino_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Executable Files (*.exe)|*.exe",
                Title = "Select Arduino IDE Executable"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TBArduinoPath.Text = openFileDialog.FileName;
            }
        }

        private void BtnBrowseQuartus_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Executable Files (*.exe)|*.exe",
                Title = "Select Quartus IDE Executable"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TBQuartusPath.Text = openFileDialog.FileName;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.ArduinoPath = TBArduinoPath.Text;
            Properties.Settings.Default.QuartusPath = TBQuartusPath.Text;
            Properties.Settings.Default.Save();
            MessageBox.Show("Настройки сохранены успешно.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}