using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;


namespace SignalCraftApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private long _count; // Число полученных пакетов

        private List<Pin> _pins = new List<Pin>(); // Список пинов выбранной платы
        private Pin _currentPin; // Выбранный пин


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
            // Загрузка доступных камеры
            _cameraManager = new CameraManager(CBCameras);

            // Загрузка com-портов
            BtnUpdatePorts_Click(null, null);
            _serialManager.DataReceived += DataReceivedHandler;

            // Добавление пинов в таблицу
            for (int i = 0; i < 36; i++)
            {
                Pin pin = new Pin()
                {
                    Id = i,
                    Name = $"GPIO_[{i}]",
                    Type = "Не задан",
                    Value = "Не задано"
                };
                _pins.Add(pin);
            }
            DGPins.ItemsSource = _pins;

        }



        private void Window_Closed(object sender, EventArgs e)
        {
            try
            {
                _serialManager.CloseSerialPort();

                _cameraManager.StopCamera();

                if (_logManager != null)
                    _logManager.CloseLogFile();
            }
            catch (Exception ex)
            {
                if (_logManager != null)
                {
                    _logManager.Log(ex.Message, false);
                    _logManager.CloseLogFile();
                }
            }

        }


        private void UpdateUI()
        {
            bool isOpenPort = _serialManager.IsOpen();

            BtnAction.Background = new SolidColorBrush(isOpenPort ? System.Windows.Media.Colors.LightGreen : System.Windows.Media.Colors.White);
            CBSpeed.IsEnabled = !isOpenPort;
            CBPorts.IsEnabled = !isOpenPort;
            BtnUpdatePorts.IsEnabled = !isOpenPort;
            GBGeneration.IsEnabled = isOpenPort;

            bool isRunningCamera = _cameraManager.IsRunning();


            CBCameras.IsEnabled = !isRunningCamera;
            BtnSearchCameras.IsEnabled = !isRunningCamera;
            BtnDownload.IsEnabled = isRunningCamera;

            if (isRunningCamera)
            {
                BtnCamStartAndStop.Background = new SolidColorBrush(System.Windows.Media.Colors.LightGreen);
                (BtnCamStartAndStop.Content as System.Windows.Controls.Image).Source = new BitmapImage(new Uri(StopImagePath, UriKind.RelativeOrAbsolute));
            }
            else
            {
                (BtnCamStartAndStop.Content as System.Windows.Controls.Image).Source = new BitmapImage(new Uri(StartImagePath, UriKind.RelativeOrAbsolute));
                BtnCamStartAndStop.Background = new SolidColorBrush(System.Windows.Media.Colors.White);
                ImCamera.Source = new BitmapImage(new Uri(CameraImagePath, UriKind.RelativeOrAbsolute));
            }


        }



        private void DGPins_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _currentPin = DGPins.SelectedItem as Pin;
            UpdatePinUI();
        }

        private void BtnBinary_Click(object sender, RoutedEventArgs e)
        {
            _currentPin.Type = "Дискретный";
            _currentPin.Value = (sender as Button).Content.ToString();

            int curPinInd = _pins.IndexOf(_currentPin);
            _pins[curPinInd].Type = _currentPin.Type;
            _pins[curPinInd].Value = _currentPin.Value;

            DGPins.ItemsSource = null;
            DGPins.ItemsSource = _pins;
            DGPins.SelectedIndex = curPinInd;

            UpdatePinUI();
        }

        private void UpdatePinUI()
        {
            DataContext = null;
            DataContext = _currentPin;
        }

        private string SendPacket(Packet packet)
        {
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
            byte[] data = new byte[5];

            foreach (Pin pin in _pins)
            {
                if (pin.Type == "Дискретный" && pin.Value == "1")
                {
                    int byteIndex = pin.Id / 8; // Номер байта (0-4)
                    int bitIndex = pin.Id % 8;   // Номер бита внутри байта (0-7)

                    data[byteIndex] |= (byte)(1 << bitIndex);
                }
            }

            Packet packet = new Packet(0x01, 0x00, 0x00, data);

            string res = SendPacket(packet);

            _logManager.Log(packet.ToString(), false, res);
        }

        #region ComRegion

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
            if (_serialManager.IsOpen())
            {
                _serialManager.SendData(TBSend.Text);
                _logManager.Log(TBSend.Text, false);
                TBSend.Clear();
            }
        }
        private void TBSend_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
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
                    (BtnAction.Content as System.Windows.Controls.Image).Source = new BitmapImage(new Uri(StopImagePath, UriKind.RelativeOrAbsolute));

                    UpdateUI();
                }
            }
            else
            {
                _serialManager.CloseSerialPort();
                (BtnAction.Content as System.Windows.Controls.Image).Source = new BitmapImage(new Uri(StartImagePath, UriKind.RelativeOrAbsolute));
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
            // Обновление UI должно происходить в UI-потоке
            Dispatcher.Invoke(() =>
            {
                ProcessReceivedData(data);
            });
        }
        #endregion

        #region CameraRegion

        private void BtnCamStartAndStop_Click(object sender, RoutedEventArgs e)
        {
            if (_cameraManager.IsRunning())
            {
                _cameraManager.StopCamera();
                Thread.Sleep(500); // камера не успевает выключиться перед обновлением интерфейса 
                UpdateUI();
                return;
            }

            _cameraManager.StartCamera(CBCameras.SelectedIndex, ImCamera);
            UpdateUI();
        }
        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (ImCamera.Source is BitmapSource bitmapSource)
            {
                // Открываем диалоговое окно сохранения файла
                System.Windows.Forms.SaveFileDialog saveFileDialog = new System.Windows.Forms.SaveFileDialog
                {
                    Filter = "PNG Files (*.png)|*.png|JPEG Files (*.jpg)|*.jpg|Bitmap Files (*.bmp)|*.bmp",
                    DefaultExt = ".png",
                    FileName = "image"
                };

                if (saveFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string filePath = saveFileDialog.FileName;

                    // Определяем формат файла
                    BitmapEncoder encoder;
                    switch (System.IO.Path.GetExtension(filePath).ToLower())
                    {
                        case ".jpg":
                            encoder = new JpegBitmapEncoder();
                            break;
                        case ".bmp":
                            encoder = new BmpBitmapEncoder();
                            break;
                        default:
                            encoder = new PngBitmapEncoder();
                            break;
                    }

                    // Добавляем кадр с изображением
                    encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

                    // Сохраняем файл
                    try
                    {
                        using (FileStream fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            encoder.Save(fileStream);
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
        private void BtnSearchCameras_Click(object sender, RoutedEventArgs e)
        {
            _cameraManager = new CameraManager(CBCameras);
        }
        #endregion

        #region Ещё не сделано


        private void BtnProg_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button).Content.ToString() == "Arduino IDE")
            {
                // Получаем путь к папке "Program Files" в зависимости от архитектуры системы
                string programFilesPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string arduinoIdePath = System.IO.Path.Combine(programFilesPath, "Arduino", "arduino.exe");

                // Проверяем, существует ли файл
                if (!File.Exists(arduinoIdePath))
                {
                    // Если не найден в "Program Files", проверяем в "Program Files (x86)"
                    programFilesPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    arduinoIdePath = System.IO.Path.Combine(programFilesPath, "Arduino", "arduino.exe");
                }

                try
                {
                    // Проверяем, существует ли файл Arduino IDE
                    if (File.Exists(arduinoIdePath))
                    {
                        // Создаем новый процесс
                        Process process = new Process();
                        process.StartInfo.FileName = arduinoIdePath;

                        // Запускаем процесс
                        process.Start();

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

        private void BtnFullScreen_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Данный функционал ещё не реализован.");
        }

        #endregion

    }
}
