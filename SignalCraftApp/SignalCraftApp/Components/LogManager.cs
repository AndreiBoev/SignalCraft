using System;
using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace SignalCraftApp
{
    public class LogManager
    {
        private string _logFilePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolderOption.Create) + @"\SignalCraftAppLogs\"; 

        private TextBox _tbData; // Поле для текстового поля, используемого для отображения логов

        public LogManager(TextBox tbData)
        {
            _tbData = tbData;
            InitializeLogFile();
        }

        private void InitializeLogFile()
        {
            // Создание директории, если она не существует
            string directoryPath = System.IO.Path.GetDirectoryName(_logFilePath);
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            _logFilePath += DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ".txt";

            if (!File.Exists(_logFilePath))
            {
                using (var stream = File.Create(_logFilePath))
                {
                    // Начальное сообщение в файле лога
                    byte[] info = new UTF8Encoding(true).GetBytes("Log file initialized on: " + DateTime.Now.ToString() + Environment.NewLine);
                    stream.Write(info, 0, info.Length);
                }
            }
        }

        public void CloseLogFile()
        {
            string message = "Log file closed on: " + DateTime.Now.ToString();
            File.AppendAllText(_logFilePath, message);

            _tbData.Clear();
        }

        public void Log(string message, bool direction, string status = "")
        {
            message.TrimEnd();

            if (direction)
                message = DateTime.Now.ToString("HH:mm:ss") + " <- " + message;
            else
                message = DateTime.Now.ToString("HH:mm:ss") + " -> " + message;

            if (!string.IsNullOrEmpty(status))
                message += " -> " + status;

            message += Environment.NewLine;

            File.AppendAllText(_logFilePath, message);

            _tbData.AppendText(message);
        }
    }
}