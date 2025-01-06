using AForge.Video;
using AForge.Video.DirectShow;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SignalCraftApp
{
    public class CameraManager
    {
        private FilterInfoCollection _videoDevices;
        private VideoCaptureDevice _videoSource;
        private Dispatcher _dispatcher;

        public CameraManager(ComboBox comboBox)
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            InitializeCameras(comboBox);
        }

        private void InitializeCameras(ComboBox cameraComboBox)
        {
            cameraComboBox.Items.Clear();
            _videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            foreach (FilterInfo device in _videoDevices)
            {
                cameraComboBox.Items.Add(device.Name);
            }

            if (cameraComboBox.Items.Count > 0)
            {
                cameraComboBox.SelectedIndex = 0;
            }
            else
            {
                cameraComboBox.Items.Add("Камеры не найдены");
            }
        }

        public void StartCamera(int cameraIndex, Image cameraImage)
        {
            if (_videoDevices.Count == 0)
            {
                MessageBox.Show("Нет доступных камер!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _videoSource = new VideoCaptureDevice(_videoDevices[cameraIndex].MonikerString);
            _videoSource.NewFrame += (sender, eventArgs) => ProcessNewFrame(eventArgs, cameraImage);
            _videoSource.Start();
        }

        public void StopCamera()
        {
            if (_videoSource != null && _videoSource.IsRunning)
            {
                _videoSource.SignalToStop();
                _videoSource.WaitForStop();
                _videoSource = null;
            }
        }

        public bool IsRunning()
        {
            if (_videoSource != null)
                return _videoSource.IsRunning;
            else
                return false;
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private void ProcessNewFrame(NewFrameEventArgs eventArgs, Image cameraImage)
        {
            var bitmap = eventArgs.Frame;

            _dispatcher.Invoke(() =>
            {
                var hBitmap = bitmap.GetHbitmap();
                try
                {
                    var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        System.Windows.Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    cameraImage.Source = bitmapSource;
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            });
        }
    }
}