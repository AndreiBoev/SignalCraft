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
using System.Xml.Linq;

namespace SignalCraftApp
{
    public partial class MainWindow : Window
    {
        private long _count;
        private List<Pin> _pins;
        private Pin _currentPin;
        private List<Pin> _queuePost = new List<Pin>();
        private FullScreenWindow fullScreenWindow;

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
            _cameraManager = new CameraManager(CBCameras, UpdateCameraImage);
            BtnUpdatePorts_Click(null, null);
            _serialManager.DataReceived += DataReceivedHandler;

            CBType.ItemsSource = Enum.GetValues(typeof(SignalType)).Cast<SignalType>().Skip(1).ToList();

            // Инициализация пинов для платы DE10-Lite
            string jsonPath = @"Configurations\DE10Lite.json";
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("Конфигурация DE10-Lite не найдена.");
            string jsonContent = File.ReadAllText(jsonPath);
            _pins = JsonConvert.DeserializeObject<List<Pin>>(jsonContent);
            CBType_SelectionChanged(null, null); // ???
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
                var newImageSource = new BitmapImage(new Uri(CameraImagePath, UriKind.RelativeOrAbsolute));
                ImCamera.Source = newImageSource;
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

        private byte[] BitmapSourceToByteArray(string imagePath)
        {
            BitmapImage bitmapImage = new BitmapImage(new Uri(imagePath));
            JpegBitmapEncoder encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapImage));

            using (MemoryStream ms = new MemoryStream())
            {
                encoder.Save(ms);
                return ms.ToArray();
            }
        }

        private void UpdateCameraImage(ImageSource newImageSource)
        {
            ImCamera.Source = newImageSource;
            if (fullScreenWindow != null)
            {
                fullScreenWindow.ImageSource = newImageSource;
            }
        }

        private void BtnSet_Click(object sender, RoutedEventArgs e)
        {
            BtnSet.IsEnabled = false;

            // VGA cигнал
            var vgaPin = _queuePost.FirstOrDefault(p => p.SelectedType == SignalType.VGA);
            if (vgaPin != null)
            {
                byte[] imageData = BitmapSourceToByteArray(vgaPin.Value);

                byte[] data = new byte[imageData.Length + 1];
                data[0] = (byte)vgaPin.Id;
                Array.Copy(imageData, 0, data, 1, imageData.Length);

                Packet vgaPacket = new Packet(0x08, 0x00, 0x00, data);
                string res = SendPacket(vgaPacket);
                _logManager.Log(vgaPacket.ToString(), false, res);

                _queuePost.Remove(vgaPin);
                BtnSet.IsEnabled = true;
                return;
            }

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

            // UART сигнал
            var uartPins = _queuePost.Where(p => p.SelectedType == SignalType.UART);
            if (uartPins != null && uartPins.Any())
            {
                foreach (Pin pin in uartPins)
                {
                    string hexText = pin.Value.Replace(" ", "").ToLower();

                    byte[] data = new byte[hexText.Length / 2 + 1];
                    data[0] = (byte)pin.Id;

                    for (int i = 0; i < hexText.Length / 2; i++)
                    {
                        string byteValue = hexText.Substring(i * 2, 2);
                        data[i + 1] = Convert.ToByte(byteValue, 16);
                    }

                    Packet uartPacket = new Packet(0x04, 0x00, 0x00, data);
                    string res = SendPacket(uartPacket);
                    _logManager.Log(uartPacket.ToString(), false, res);
                }
            }

            // SPI сигнал
            var spiPins = _queuePost.Where(p => p.SelectedType == SignalType.SPI);
            if (spiPins != null && spiPins.Any())
            {
                foreach (Pin pin in spiPins)
                {
                    string hexText = pin.Value.Replace(" ", "").ToLower();

                    byte[] data = new byte[hexText.Length / 2 + 1];
                    data[0] = (byte)pin.Id;

                    for (int i = 0; i < hexText.Length / 2; i++)
                    {
                        string byteValue = hexText.Substring(i * 2, 2);
                        data[i + 1] = Convert.ToByte(byteValue, 16);
                    }

                    Packet spiPacket = new Packet(0x05, 0x00, 0x00, data);
                    string res = SendPacket(spiPacket);
                    _logManager.Log(spiPacket.ToString(), false, res);
                }
            }

            // I2C сигнал
            var i2cPins = _queuePost.Where(p => p.SelectedType == SignalType.I2C);
            if (i2cPins != null && i2cPins.Any())
            {
                foreach (Pin pin in i2cPins)
                {
                    string hexText = pin.Value.Replace(" ", "").ToLower();

                    byte[] data = new byte[hexText.Length / 2 + 1];
                    data[0] = (byte)pin.Id;

                    for (int i = 0; i < hexText.Length / 2; i++)
                    {
                        string byteValue = hexText.Substring(i * 2, 2);
                        data[i + 1] = Convert.ToByte(byteValue, 16);
                    }

                    Packet i2cPacket = new Packet(0x06, 0x00, 0x00, data);
                    string res = SendPacket(i2cPacket);
                    _logManager.Log(i2cPacket.ToString(), false, res);
                }
            }

            // PS2 сигнал
            var ps2Pins = _queuePost.Where(p => p.SelectedType == SignalType.PS2);
            if (ps2Pins != null && ps2Pins.Any())
            {
                foreach (Pin pin in ps2Pins)
                {
                    string hexText = pin.Value.Replace(" ", "").ToLower();

                    byte[] data = new byte[hexText.Length / 2 + 1];
                    data[0] = (byte)pin.Id;

                    for (int i = 0; i < hexText.Length / 2; i++)
                    {
                        string byteValue = hexText.Substring(i * 2, 2);
                        data[i + 1] = Convert.ToByte(byteValue, 16);
                    }

                    Packet ps2Packet = new Packet(0x07, 0x00, 0x00, data);
                    string res = SendPacket(ps2Packet);
                    _logManager.Log(ps2Packet.ToString(), false, res);
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
                        await Task.Run(() =>
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                using (FileStream fileStream = new FileStream(filePath, FileMode.Create))
                                {
                                    encoder.Save(fileStream);
                                }
                            });
                        });
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
            _cameraManager = new CameraManager(CBCameras, UpdateCameraImage);
        }

        private void BtnProg_Click(object sender, RoutedEventArgs e)
        {
            string idePath = string.Empty;
            string ideName = (sender as Button).Content.ToString();

            if (ideName == "Arduino IDE")
            {
                idePath = Properties.Settings.Default.ArduinoPath;
            }
            else if (ideName == "Quartus Prime")
            {
                idePath = Properties.Settings.Default.QuartusPath;
            }

            try
            {
                if (File.Exists(idePath))
                {
                    Process.Start(idePath);
                    MessageBox.Show($"{ideName} запущена.");
                }
                else
                {
                    MessageBox.Show($"{ideName} не найдена. Проверьте установку.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при запуске {ideName}: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnFullScreen_Click(object sender, RoutedEventArgs e)
        {
            if (fullScreenWindow == null)
            {
                fullScreenWindow = new FullScreenWindow();
                fullScreenWindow.Closed += (s, args) => fullScreenWindow = null;
                fullScreenWindow.Show();
            }
            fullScreenWindow.ImageSource = ImCamera.Source;
        }

        private void CBType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_pins != null)
            {
                UpdateDGPins();
                DGPins.SelectedIndex = 0;
                switch (CBType.SelectedItem)
                {
                    case SignalType.Digital:
                        GridDigital.Visibility = Visibility.Visible;
                        GridAnalog.Visibility = Visibility.Collapsed;
                        GridPWM.Visibility = Visibility.Collapsed;
                        GridUART.Visibility = Visibility.Collapsed;
                        GridSPI.Visibility = Visibility.Collapsed;
                        GridI2C.Visibility = Visibility.Collapsed;
                        GridPS2.Visibility = Visibility.Collapsed;
                        GridVGA.Visibility = Visibility.Collapsed;
                        break;
                    case SignalType.Analog:
                        GridDigital.Visibility = Visibility.Collapsed;
                        GridAnalog.Visibility = Visibility.Visible;
                        GridPWM.Visibility = Visibility.Collapsed;
                        GridUART.Visibility = Visibility.Collapsed;
                        GridSPI.Visibility = Visibility.Collapsed;
                        GridI2C.Visibility = Visibility.Collapsed;
                        GridPS2.Visibility = Visibility.Collapsed;
                        GridVGA.Visibility = Visibility.Collapsed;
                        break;
                    case SignalType.PWM:
                        GridDigital.Visibility = Visibility.Collapsed;
                        GridAnalog.Visibility = Visibility.Collapsed;
                        GridPWM.Visibility = Visibility.Visible;
                        GridUART.Visibility = Visibility.Collapsed;
                        GridSPI.Visibility = Visibility.Collapsed;
                        GridI2C.Visibility = Visibility.Collapsed;
                        GridPS2.Visibility = Visibility.Collapsed;
                        GridVGA.Visibility = Visibility.Collapsed;
                        break;
                    case SignalType.UART:
                        GridDigital.Visibility = Visibility.Collapsed;
                        GridAnalog.Visibility = Visibility.Collapsed;
                        GridPWM.Visibility = Visibility.Collapsed;
                        GridUART.Visibility = Visibility.Visible;
                        GridSPI.Visibility = Visibility.Collapsed;
                        GridI2C.Visibility = Visibility.Collapsed;
                        GridPS2.Visibility = Visibility.Collapsed;
                        GridVGA.Visibility = Visibility.Collapsed;
                        break;
                    case SignalType.SPI:
                        GridDigital.Visibility = Visibility.Collapsed;
                        GridAnalog.Visibility = Visibility.Collapsed;
                        GridPWM.Visibility = Visibility.Collapsed;
                        GridUART.Visibility = Visibility.Collapsed;
                        GridSPI.Visibility = Visibility.Visible;
                        GridI2C.Visibility = Visibility.Collapsed;
                        GridPS2.Visibility = Visibility.Collapsed;
                        GridVGA.Visibility = Visibility.Collapsed;
                        break;
                    case SignalType.I2C:
                        GridDigital.Visibility = Visibility.Collapsed;
                        GridAnalog.Visibility = Visibility.Collapsed;
                        GridPWM.Visibility = Visibility.Collapsed;
                        GridUART.Visibility = Visibility.Collapsed;
                        GridSPI.Visibility = Visibility.Collapsed;
                        GridI2C.Visibility = Visibility.Visible;
                        GridPS2.Visibility = Visibility.Collapsed;
                        GridVGA.Visibility = Visibility.Collapsed;
                        break;
                    case SignalType.PS2:
                        GridDigital.Visibility = Visibility.Collapsed;
                        GridAnalog.Visibility = Visibility.Collapsed;
                        GridPWM.Visibility = Visibility.Collapsed;
                        GridUART.Visibility = Visibility.Collapsed;
                        GridSPI.Visibility = Visibility.Collapsed;
                        GridI2C.Visibility = Visibility.Collapsed;
                        GridPS2.Visibility = Visibility.Visible;
                        GridVGA.Visibility = Visibility.Collapsed;
                        break;
                    case SignalType.VGA:
                        GridDigital.Visibility = Visibility.Collapsed;
                        GridAnalog.Visibility = Visibility.Collapsed;
                        GridPWM.Visibility = Visibility.Collapsed;
                        GridUART.Visibility = Visibility.Collapsed;
                        GridSPI.Visibility = Visibility.Collapsed;
                        GridI2C.Visibility = Visibility.Collapsed;
                        GridPS2.Visibility = Visibility.Collapsed;
                        GridVGA.Visibility = Visibility.Visible;
                        break;
                }
            }
        }

        private void UpdateDGPins()
        {
            if (CBType.SelectedItem is SignalType type)
            {
                var filteredPins = _pins.Where(pin => pin.SupportedSignals.Contains(type)).ToList();
                DGPins.ItemsSource = filteredPins;
                DGPins.SelectedItem = _currentPin;
            }
        }

        private void PushPin()
        {
            if (!_queuePost.Contains(_currentPin))
            {
                _queuePost.Add(_currentPin);
            }
        }

        private void BtnBinary_Click(object sender, RoutedEventArgs e)
        {
            _currentPin.SelectedType = SignalType.Digital;
            _currentPin.Value = (sender as Button).Content.ToString();

            PushPin();
            UpdateDGPins();
            UpdatePinUI();
        }

        private void SAnalog_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _currentPin.SelectedType = SignalType.Analog;
            _currentPin.Value = SAnalog.Value.ToString();

            PushPin();
            UpdateDGPins();
            UpdatePinUI();
        }

        private void SPWM_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _currentPin.SelectedType = SignalType.PWM;
            _currentPin.Value = SPWM.Value.ToString() + '%';

            PushPin();
            UpdateDGPins();
            UpdatePinUI();
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            SAnalog.Value = 0;
            SPWM.Value = 0;
            TBUART.Clear();
            TBSPI.Clear();
            TBI2C.Clear();
            TBPS2.Clear();
            TBlockVGA.Text = "";
            ImgPreview.Source = null;
            _queuePost.Clear();
            Window_Loaded(null, null);
        }

        private void TBUART_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                string hexText = TBUART.Text.Replace(" ", "").ToLower();
                if (System.Text.RegularExpressions.Regex.IsMatch(hexText, "^[0-9a-f]*$"))
                {
                    byte[] bytes = Enumerable.Range(0, hexText.Length)
                        .Where(x => x % 2 == 0)
                        .Select(x => Convert.ToByte(hexText.Substring(x, 2), 16))
                        .ToArray();

                    _currentPin.SelectedType = SignalType.UART;
                    _currentPin.Value = TBUART.Text;

                    PushPin();
                    UpdateDGPins();
                    UpdatePinUI();
                }
            }
            catch { }
        }

        private void TBSPI_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                string hexText = TBSPI.Text.Replace(" ", "").ToLower();
                if (System.Text.RegularExpressions.Regex.IsMatch(hexText, "^[0-9a-f]*$"))
                {
                    byte[] bytes = Enumerable.Range(0, hexText.Length)
                        .Where(x => x % 2 == 0)
                        .Select(x => Convert.ToByte(hexText.Substring(x, 2), 16))
                        .ToArray();

                    _currentPin.SelectedType = SignalType.UART;
                    _currentPin.Value = TBSPI.Text;

                    PushPin();
                    UpdateDGPins();
                    UpdatePinUI();
                }
            }
            catch { }
        }

        private void TBI2C_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                string hexText = TBI2C.Text.Replace(" ", "").ToLower();
                if (System.Text.RegularExpressions.Regex.IsMatch(hexText, "^[0-9a-f]*$"))
                {
                    byte[] bytes = Enumerable.Range(0, hexText.Length)
                        .Where(x => x % 2 == 0)
                        .Select(x => Convert.ToByte(hexText.Substring(x, 2), 16))
                        .ToArray();

                    _currentPin.SelectedType = SignalType.UART;
                    _currentPin.Value = TBI2C.Text;

                    PushPin();
                    UpdateDGPins();
                    UpdatePinUI();
                }
            }
            catch { }
        }

        private void TBPS2_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                string hexText = TBPS2.Text.Replace(" ", "").ToLower();
                if (System.Text.RegularExpressions.Regex.IsMatch(hexText, "^[0-9a-f]*$"))
                {
                    byte[] bytes = Enumerable.Range(0, hexText.Length)
                        .Where(x => x % 2 == 0)
                        .Select(x => Convert.ToByte(hexText.Substring(x, 2), 16))
                        .ToArray();

                    _currentPin.SelectedType = SignalType.UART;
                    _currentPin.Value = TBPS2.Text;

                    PushPin();
                    UpdateDGPins();
                    UpdatePinUI();
                }
            }
            catch { }
        }


        private void BtnLoadImage_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image Files (*.bmp;*.jpg;*.png)|*.bmp;*.jpg;*.png",
                Title = "Select an Image File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var bitmap = new BitmapImage(new Uri(openFileDialog.FileName));
                    ImgPreview.Source = bitmap;
                    TBlockVGA.Text = openFileDialog.FileName;

                    _currentPin.SelectedType = SignalType.VGA;
                    _currentPin.Value = openFileDialog.FileName;
                    PushPin();
                    UpdateDGPins();
                    UpdatePinUI();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading image: {ex.Message}", "Error",
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow settingsWindow = new SettingsWindow();
            settingsWindow.Owner = this;
            settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            settingsWindow.ShowDialog();
        }
    }

}

