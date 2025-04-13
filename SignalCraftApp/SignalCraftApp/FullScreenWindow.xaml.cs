using System.Windows;
using System.Windows.Input;

namespace SignalCraftApp
{
    public partial class FullScreenWindow : Window
    {
        public FullScreenWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        public static readonly DependencyProperty ImageSourceProperty =
            DependencyProperty.Register("ImageSource", typeof(System.Windows.Media.ImageSource), typeof(FullScreenWindow));

        public System.Windows.Media.ImageSource ImageSource
        {
            get { return (System.Windows.Media.ImageSource)GetValue(ImageSourceProperty); }
            set { SetValue(ImageSourceProperty, value); }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }
    }
}

