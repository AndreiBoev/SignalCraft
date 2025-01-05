using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

using AForge.Video;
using AForge.Video.DirectShow;


namespace SignalCraftApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private FilterInfoCollection _videoDevices; // Список доступных камер
        private VideoCaptureDevice _videoSource;    // Устройство видеозахвата
        private SerialPort _port = new SerialPort(); // Com-порт
        private long _count; // Число полученных пакетов
        private string _path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolderOption.Create) + @"\SignalCraftAppLogs\"; // Путь для сохранения
        private List<Pin> _pins = new List<Pin>(); // Список пинов выбранной платы
        private Pin _currentPin; // Выбранный пин


        private const string CameraImagePath = @"/Resources/Icons/Camera.png";
        private const string StartImagePath = @"/Resources/Icons/Start.png";
        private const string StopImagePath = @"/Resources/Icons/Stop.png";

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _currentPin;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Загружаем доступные камеры
            BtnSearchCameras_Click(null, null);

            // Загрузка com-портов
            BtnUpdatePorts_Click(null, null);
            _port.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);

            // Проверка существования директории для логов
            DirectoryInfo dirInfo = new DirectoryInfo(_path);
            if (!dirInfo.Exists)
            {
                dirInfo.Create();
            }

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


        private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            // Этот код выполняется в потоке, отличном от UI
            try
            {
                string receivedData = _port.ReadLine();
                // Обновление UI должно происходить в UI-потоке
                Dispatcher.Invoke(() =>
                {
                    ProcessReceivedData(receivedData);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Ошибка при чтении данных из COM-порта: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                });

            }

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
                Log(data, true, "OK");
            }
            catch (Exception ex)
            {
                Log(data, true, ex.Message);
            }

            if (CBAutoScroll.IsChecked.Value)
                TBData.ScrollToEnd();
        }

        private void BtnSearchCameras_Click(object sender, RoutedEventArgs e)
        {
            CBCameras.Items.Clear();

            try
            {
                // Получаем все доступные устройства видеозахвата (камеры)
                _videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

                if (_videoDevices.Count == 0)
                {
                    MessageBox.Show("Камеры не найдены", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    CBCameras.Items.Add("Камеры не найдены");
                    return;
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при поиске камер: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            // Отображаем список камер в ComboBox
            foreach (FilterInfo device in _videoDevices)
            {
                CBCameras.Items.Add(device.Name);
            }
            CBCameras.SelectedIndex = 0;
        }


        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        // Обработчик нового кадра
        private void videoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            var bitmap = eventArgs.Frame;

            // Конвертируем полученный кадр в BitmapImage для отображения в WPF
            Dispatcher.Invoke(() =>
            {
                var hBitmap = bitmap.GetHbitmap();
                try
                {
                    var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        System.Windows.Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    // Отображаем видео в Image
                    ImCamera.Source = bitmapSource;
                }
                finally
                {
                    // Освобождаем HBitmap
                    DeleteObject(hBitmap);
                }
            });
        }

        private void BtnCamStartAndStop_Click(object sender, RoutedEventArgs e)
        {
            if (_videoSource != null && _videoSource.IsRunning)
            {
                _videoSource.SignalToStop();
                Thread.Sleep(500); // камера не успевает выключиться перед обновлением интерфейса 
                UpdateUI();
                return;
            }
            _videoSource = new VideoCaptureDevice(_videoDevices[CBCameras.SelectedIndex].MonikerString);
            _videoSource.NewFrame += new NewFrameEventHandler(videoSource_NewFrame);
            _videoSource.Start();
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

        private void Window_Closed(object sender, EventArgs e)
        {
            try
            {
                _port.Close();

                // Выключаем камеру, если она не выключена
                if (_videoSource != null && _videoSource.IsRunning)
                    BtnCamStartAndStop_Click(null, null);

                Log("End", false);
            }
            catch (Exception ex)
            {
                Log(ex.Message, false);
            }

        }

        private void BtnUpdatePorts_Click(object sender, RoutedEventArgs e)
        {
            CBPorts.Items.Clear();
            string[] ports = SerialPort.GetPortNames();
            if (ports.Length == 0)
            {
                CBPorts.Items.Add("Не найдено");
                CBPorts.SelectedIndex = 0;
                BtnAction.IsEnabled = false;
                return;
            }
            foreach (string port in ports)
            {
                CBPorts.Items.Add(port);
            }
            CBPorts.SelectedIndex = ports.Length - 1;
            BtnAction.IsEnabled = true;
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            if (_port.IsOpen)
            {
                _port.Write(TBSend.Text);
                Log(TBSend.Text, false);
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
            if (!_port.IsOpen)
            {
                try
                {
                    _path += DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ".txt";

                    _port.PortName = CBPorts.Text;
                    _port.BaudRate = int.Parse(CBSpeed.Text);
                    _port.Open();
                    (BtnAction.Content as System.Windows.Controls.Image).Source = new BitmapImage(new Uri(StopImagePath, UriKind.RelativeOrAbsolute));

                    UpdateUI();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    BtnUpdatePorts_Click(null, null);
                }
            }
            else
            {
                _port.Close();
                (BtnAction.Content as System.Windows.Controls.Image).Source = new BitmapImage(new Uri(StartImagePath, UriKind.RelativeOrAbsolute));
                TBData.Clear();
                UpdateUI();
                TBlockCount.Text = "0";
                _count = 0;
            }
        }

        private void UpdateUI()
        {
            BtnAction.Background = new SolidColorBrush(_port.IsOpen ? System.Windows.Media.Colors.LightGreen : System.Windows.Media.Colors.White);
            CBSpeed.IsEnabled = !_port.IsOpen;
            CBPorts.IsEnabled = !_port.IsOpen;
            BtnUpdatePorts.IsEnabled = !_port.IsOpen;
            GBGeneration.IsEnabled = _port.IsOpen;



            if (_videoSource != null)
            {
                CBCameras.IsEnabled = !_videoSource.IsRunning;
                BtnSearchCameras.IsEnabled = !_videoSource.IsRunning;
                BtnDownload.IsEnabled = _videoSource.IsRunning;
            }

            if (_videoSource != null && _videoSource.IsRunning)
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

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _count = 0;
            TBlockCount.Text = "0";
            TBData.Clear();
        }

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

        private void Log(string message, bool direction, string status = "")
        {
            message.TrimEnd();

            if (direction)
                message = DateTime.Now.ToString("HH:mm:ss") + " <- " + message;
            else
                message = DateTime.Now.ToString("HH:mm:ss") + " -> " + message;

            if (!string.IsNullOrEmpty(status))
                message += " -> " + status;

            TBData.Text += message + '\n';

            if (File.Exists(_path))
            {
                using (var writer = new StreamWriter(_path, true))
                {
                    writer.WriteLineAsync(message);
                }
            }
        }

        private string SendPacket(Packet packet)
        {
            try
            {
                _port.WriteLine(packet.ToString());
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
            Log(packet.ToString(), false, res);
        }
    }
}
