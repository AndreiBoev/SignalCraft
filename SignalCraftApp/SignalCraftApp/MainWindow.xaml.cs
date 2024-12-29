using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

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

        public MainWindow()
        {
            InitializeComponent();
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Загружаем доступные камеры
            BtnSearchCameras_Click(null, null);
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

        // Обработчик нового кадра
        private void videoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            var bitmap = eventArgs.Frame;

            // Конвертируем полученный кадр в BitmapImage для отображения в WPF
            Dispatcher.Invoke(() =>
            {
                var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                bitmap.GetHbitmap(),
                IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

                // Отображаем видео в Image
                ImCamera.Source = bitmapSource;
            });
        }

        private void BtnCamStartAndStop_Click(object sender, RoutedEventArgs e)
        {
            if (_videoSource != null && _videoSource.IsRunning)
            {
                _videoSource.SignalToStop();
                ImCamera.Source = new BitmapImage(new Uri(@"/Resources/Icons/Camera.png", UriKind.RelativeOrAbsolute));
                (BtnCamStartAndStop.Content as System.Windows.Controls.Image).Source = new BitmapImage(new Uri(@"/Resources/Icons/Start.png", UriKind.RelativeOrAbsolute));
                CBCameras.IsEnabled = true;
                BtnSearchCameras.IsEnabled = true;
                BtnDownload.IsEnabled = false;
                return;
            }
            CBCameras.IsEnabled = false;
            BtnSearchCameras.IsEnabled = false;
            BtnDownload.IsEnabled = true;
            (BtnCamStartAndStop.Content as System.Windows.Controls.Image).Source = new BitmapImage(new Uri(@"/Resources/Icons/Stop.png", UriKind.RelativeOrAbsolute));
            _videoSource = new VideoCaptureDevice(_videoDevices[CBCameras.SelectedIndex].MonikerString);
            _videoSource.NewFrame += new NewFrameEventHandler(videoSource_NewFrame);
            _videoSource.Start();
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
                    switch (Path.GetExtension(filePath).ToLower())
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
            // Выключаем камеру, если она не выключена
            if (_videoSource != null)
                BtnCamStartAndStop_Click(null, null);
        }
    }
}
