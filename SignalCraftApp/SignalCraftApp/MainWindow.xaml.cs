using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SignalCraftApp
{
    public partial class MainWindow : Window
    {
        private long _count;
        private List<Pin> _pins;
        private Pin _currentPin;
        private List<Pin> _queuePost = new List<Pin>();

        private const string CameraImagePath = @"/Resources/Icons/Camera.png";
        private const string StartImagePath = @"/Resources/Icons/Start.png";
        private const string StopImagePath = @"/Resources/Icons/Stop.png";

        private LogManager _logManager;
        private SerialManager _serialManager = new SerialManager();
        private CameraManager _cameraManager;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _currentPin;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _cameraManager = new CameraManager(CBCameras);
            BtnUpdatePorts_Click(null, null);
            _serialManager.DataReceived += DataReceivedHandler;

            CBType.ItemsSource = Enum.GetValues(typeof(SignalType)).Cast<SignalType>().Skip(1).ToList();

            // Инициализация пинов для платы DE10-Lite
            string jsonPath = @"Configurations\DE10Lite.json";
            string jsonContent = File.ReadAllText(jsonPath);
            _pins = JsonConvert.DeserializeObject<List<Pin>>(jsonContent);
            CBType_SelectionChanged(null, null);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            try
            {
                _serialManager.CloseSerialPort();
                _cameraManager.StopCamera();
                _logManager?.CloseLogFile();
            }
            catch (Exception ex)
            {
                _logManager?.Log(ex.Message, false);
                _logManager?.CloseLogFile();
            }
        }

        private void UpdateUI()
        {
            bool isOpenPort = _serialManager.IsOpen();
            bool isRunningCamera = _cameraManager.IsRunning();

            BtnAction.Background = new SolidColorBrush(isOpenPort ? Colors.LightGreen : Colors.White);
            CBSpeed.IsEnabled = !isOpenPort;
            CBPorts.IsEnabled = !isOpenPort;
            BtnUpdatePorts.IsEnabled = !isOpenPort;
            GBGeneration.IsEnabled = isOpenPort;

            CBCameras.IsEnabled = !isRunningCamera;
            BtnSearchCameras.IsEnabled = !isRunningCamera;
            BtnDownload.IsEnabled = isRunningCamera;

            UpdateButtonState(BtnCamStartAndStop, isRunningCamera, StartImagePath, StopImagePath);
            if (!isRunningCamera)
            {
                ImCamera.Source = new BitmapImage(new Uri(CameraImagePath, UriKind.RelativeOrAbsolute));
            }
        }

        private void UpdateButtonState(Button button, bool isActive, string startImagePath, string stopImagePath)
        {
            button.Background = new SolidColorBrush(isActive ? Colors.LightGreen : Colors.White);
            (button.Content as Image).Source = new BitmapImage(new Uri(isActive ? stopImagePath : startImagePath, UriKind.RelativeOrAbsolute));
        }

        private void DGPins_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _currentPin = DGPins.SelectedItem as Pin;
            UpdatePinUI();

            if (_currentPin != null && _currentPin.Value != "")
                SAnalog.Value = int.Parse(_currentPin.Value);
            else
                SAnalog.Value = 0;
        }

        private void BtnBinary_Click(object sender, RoutedEventArgs e)
        {
            if(_currentPin == null)
            {
                DGPins.SelectedIndex = 0;
            }
            _currentPin.SelectedType = SignalType.Digital;
            _currentPin.Value = (sender as Button).Content.ToString();

            PushPin();
            UpdateDGPins();
            UpdatePinUI();
        }

        private void UpdatePinUI()
        {
            DataContext = null;
            DataContext = _currentPin;
        }

        private string SendPacket(Packet packet)
        {
            if (CBAutoScroll.IsChecked.Value)
                TBData.ScrollToEnd();
            try
            {
                _serialManager.SendData(packet.ToString());
                return "OK";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }

        }

        private void BtnSet_Click(object sender, RoutedEventArgs e)
        {
            BtnSet.IsEnabled = false;

            // Дискретный сигнал
            var digitalPins = _queuePost.Where(p => p.SelectedType == SignalType.Digital);
            if (digitalPins != null && digitalPins.Any())
            {
                byte[] data = new byte[5];

                foreach (Pin pin in digitalPins)
                {
                    int byteIndex = pin.Id / 8;
                    int bitIndex = pin.Id % 8;
                    data[byteIndex] |= (byte)(1 << bitIndex);
                }

                Packet digitalPacket = new Packet(0x01, 0x00, 0x00, data);
                string res = SendPacket(digitalPacket);
                _logManager.Log(digitalPacket.ToString(), false, res);
            }

            // Аналоговый сигнал
            var analogPins = _queuePost.Where(p => p.SelectedType == SignalType.Analog);
            if (analogPins != null && analogPins.Any())
            {
                foreach (Pin pin in analogPins)
                {
                    ushort analogValue = ushort.Parse(pin.Value);
                    byte[] analogBytes = BitConverter.GetBytes(analogValue);
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(analogBytes);
                    }

                    byte[] data = new byte[3];
                    data[0] = (byte)pin.Id; 
                    data[1] = analogBytes[0];
                    data[2] = analogBytes[1];

                    Packet analogPacket = new Packet(0x03, 0x00, 0x00, data);
                    string res = SendPacket(analogPacket);
                    _logManager.Log(analogPacket.ToString(), false, res);
                }
            }

            // ШИМ сигнал
            var pwmPins = _queuePost.Where(p => p.SelectedType == SignalType.PWM);
            if (pwmPins != null && pwmPins.Any())
            {
                foreach (Pin pin in pwmPins)
                {
                    byte pwmValue = byte.Parse(pin.Value.TrimEnd('%'));
                    byte[] data = new byte[2];
                    data[0] = (byte)pin.Id;
                    data[1] = pwmValue;
                    Packet pwmPacket = new Packet(0x02, 0x00, 0x00, data);
                    string res = SendPacket(pwmPacket);
                    _logManager.Log(pwmPacket.ToString(), false, res);
                }
            }

            _queuePost.Clear();
            BtnSet.IsEnabled = true;
        }

        private void ProcessReceivedData(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
                return;

            _count++;
            TBlockCount.Text = _count.ToString();

            data = data.TrimEnd();

            try
            {
                Packet packet = new Packet(data);
                _logManager.Log(data, true, "OK");
            }
            catch (Exception ex)
            {
                _logManager.Log(data, true, ex.Message);
            }

            if (CBAutoScroll.IsChecked.Value)
                TBData.ScrollToEnd();
        }

        private void BtnUpdatePorts_Click(object sender, RoutedEventArgs e)
        {
            _serialManager.UpdatePortNames(CBPorts);
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            if (_serialManager.IsOpen() && TBSend.Text != "")
            {
                _serialManager.SendData(TBSend.Text);
                _logManager.Log(TBSend.Text, false);
                TBSend.Clear();
            }
        }

        private void TBSend_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                BtnSend_Click(null, null);
        }

        private void BtnAction_Click(object sender, RoutedEventArgs e)
        {
            if (!_serialManager.IsOpen())
            {
                _logManager = new LogManager(TBData);

                if (_serialManager.InitializeSerialPort(CBPorts.Text, int.Parse(CBSpeed.Text)))
                {
                    UpdateButtonState(BtnAction, true, StartImagePath, StopImagePath);
                    UpdateUI();
                }
            }
            else
            {
                _serialManager.CloseSerialPort();
                UpdateButtonState(BtnAction, false, StartImagePath, StopImagePath);
                _logManager.CloseLogFile();

                UpdateUI();
                TBlockCount.Text = "0";
                _count = 0;
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _count = 0;
            TBlockCount.Text = "0";
            TBData.Clear();
        }

        private void DataReceivedHandler(object sender, string data)
        {
            Dispatcher.Invoke(() => ProcessReceivedData(data));
        }

        private void BtnCamStartAndStop_Click(object sender, RoutedEventArgs e)
        {
            if (_cameraManager.IsRunning())
            {
                _cameraManager.StopCamera();
                Thread.Sleep(500);
                UpdateUI();
                return;
            }

            _cameraManager.StartCamera(CBCameras.SelectedIndex, ImCamera);
            UpdateUI();
        }

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (ImCamera.Source is BitmapSource bitmapSource)
            {
                var saveFileDialog = new System.Windows.Forms.SaveFileDialog
                {
                    Filter = "PNG Files (*.png)|*.png|JPEG Files (*.jpg)|*.jpg|Bitmap Files (*.bmp)|*.bmp",
                    DefaultExt = ".png",
                    FileName = "image"
                };

                if (saveFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string filePath = saveFileDialog.FileName;
                    BitmapEncoder encoder = GetEncoder(filePath);

                    encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

                    try
                    {
                        using (FileStream fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await Task.Run(() => encoder.Save(fileStream));
                        }
                        MessageBox.Show("Изображение успешно сохранено!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка при сохранении: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private BitmapEncoder GetEncoder(string filePath)
        {
            switch (Path.GetExtension(filePath).ToLower())
            {
                case ".jpg":
                    return new JpegBitmapEncoder();
                case ".bmp":
                    return new BmpBitmapEncoder();
                default:
                    return new PngBitmapEncoder();
            }
        }

        private void BtnSearchCameras_Click(object sender, RoutedEventArgs e)
        {
            _cameraManager = new CameraManager(CBCameras);
        }

        private void BtnProg_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button).Content.ToString() == "Arduino IDE")
            {
                string arduinoIdePath = GetArduinoIdePath();

                try
                {
                    if (File.Exists(arduinoIdePath))
                    {
                        Process.Start(arduinoIdePath);
                        MessageBox.Show("Arduino IDE запущена.");
                    }
                    else
                    {
                        MessageBox.Show("Arduino IDE не найдена. Проверьте установку.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при запуске Arduino IDE: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Данный функционал ещё не реализован.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetArduinoIdePath()
        {
            string programFilesPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string arduinoIdePath = Path.Combine(programFilesPath, "Arduino", "arduino.exe");

            if (!File.Exists(arduinoIdePath))
            {
                programFilesPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                arduinoIdePath = Path.Combine(programFilesPath, "Arduino", "arduino.exe");
            }

            return arduinoIdePath;
        }

        private void BtnFullScreen_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Данный функционал ещё не реализован.");
        }

        private void CBType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_pins != null && CBType.SelectedItem is SignalType selectedType)
            {
                var filteredPins = _pins.Where(pin => pin.SupportedSignals.Contains(selectedType)).ToList();
                DGPins.ItemsSource = filteredPins;

                switch (selectedType)
                {
                    case SignalType.Digital:
                        GridDigital.Visibility = Visibility.Visible;
                        GridAnalog.Visibility = Visibility.Collapsed;
                        GridPWM.Visibility = Visibility.Collapsed;
                        break;
                    case SignalType.Analog:
                        GridDigital.Visibility = Visibility.Collapsed;
                        GridAnalog.Visibility = Visibility.Visible;
                        GridPWM.Visibility = Visibility.Collapsed;

                        // SAnalog_ValueChanged(null, null);
                        break;
                    case SignalType.PWM:
                        GridDigital.Visibility = Visibility.Collapsed;
                        GridAnalog.Visibility = Visibility.Collapsed;
                        GridPWM.Visibility = Visibility.Visible;

                        // SPWM_ValueChanged(null, null);
                        break;
                    case SignalType.UART:
                        GridDigital.Visibility = Visibility.Collapsed;
                        GridAnalog.Visibility = Visibility.Collapsed;
                        GridPWM.Visibility = Visibility.Collapsed;
                        break;
                    case SignalType.SPI:
                        GridDigital.Visibility = Visibility.Collapsed;
                        GridAnalog.Visibility = Visibility.Collapsed;
                        GridPWM.Visibility = Visibility.Collapsed;
                        break;
                    case SignalType.I2C:
                        GridDigital.Visibility = Visibility.Collapsed;
                        GridAnalog.Visibility = Visibility.Collapsed;
                        GridPWM.Visibility = Visibility.Collapsed;
                        break;
                    case SignalType.PS2:
                        GridDigital.Visibility = Visibility.Collapsed;
                        GridAnalog.Visibility = Visibility.Collapsed;
                        GridPWM.Visibility = Visibility.Collapsed;
                        break;
                    case SignalType.VGA:
                        GridDigital.Visibility = Visibility.Collapsed;
                        GridAnalog.Visibility = Visibility.Collapsed;
                        GridPWM.Visibility = Visibility.Collapsed;
                        break;
                }
            }
        }

        private void PushPin()
        {
            if (!_queuePost.Contains(_currentPin))
            {
                _queuePost.Add(_currentPin);
            }
        }

        private void UpdateDGPins()
        {
            var filteredPins = _pins.Where(pin => pin.SupportedSignals.Contains(_currentPin.SelectedType)).ToList();
            DGPins.ItemsSource = filteredPins;
        }

        private void SAnalog_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_currentPin == null)
            {
                DGPins.SelectedIndex = 0;
            }
            _currentPin.SelectedType = SignalType.Analog;
            _currentPin.Value = SAnalog.Value.ToString();

            PushPin();
            UpdateDGPins();
            UpdatePinUI();
        }

        private void SPWM_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_currentPin == null)
            {
                DGPins.SelectedIndex = 0;
            }
            _currentPin.SelectedType = SignalType.PWM;
            _currentPin.Value = SPWM.Value.ToString() + '%';

            PushPin();
            UpdateDGPins();
            UpdatePinUI();
        }
    }
}
