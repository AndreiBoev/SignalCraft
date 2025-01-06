using System;
using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace SignalCraftApp
{
    public class SerialManager
    {
        private SerialPort _serialPort;

        public event EventHandler<string> DataReceived; // Событие для получения данных

        public bool InitializeSerialPort(string portName, int baudRate)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                CloseSerialPort();
            }

            try
            {
                _serialPort = new SerialPort(portName, baudRate);
                _serialPort.DataReceived += OnDataReceived; // Подписка на получение данных
                _serialPort.Open();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии порта: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            return true;
        }

        public void UpdatePortNames(ComboBox comboBox)
        {
            comboBox.Items.Clear();
            string[] ports = SerialPort.GetPortNames();
            if (ports.Length == 0)
            {
                comboBox.Items.Add("Не найдено");
                comboBox.SelectedIndex = 0;
            }
            foreach (string port in ports)
            {
                comboBox.Items.Add(port);
            }
            comboBox.SelectedIndex = ports.Length - 1;
        }

        public void CloseSerialPort()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.DataReceived -= OnDataReceived;
                _serialPort.Close();
                _serialPort.Dispose(); // Освобождаем ресурсы
                _serialPort = null;
            }
        }

        public bool IsOpen()
        {
            if (_serialPort != null)
                return _serialPort.IsOpen;
            else
                return false;
        }

        public void SendData(string data)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                try
                {
                    _serialPort.WriteLine(data);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при отправке данных: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Порт не открыт.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            string data = string.Empty;
            try
            {
                // Читаем данные в формате string
                data = _serialPort.ReadLine();
                // Вызываем событие для получения данных
                DataReceived?.Invoke(this, data);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при получении данных: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}