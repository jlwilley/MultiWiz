using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using static MultiWiz.MainWindow;
using System.Runtime.InteropServices;


namespace MultiWiz
{

    /// <summary>
    /// Interaction logic for Switcher.xaml
    /// </summary>
    public partial class Switcher : Window
    {
        private MainWindow mainWindow;

        public Switcher(MainWindow mw)
        {
            mainWindow = mw; 
            InitializeComponent();
            AddButtons();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            mainWindow.Show();
            this.Close();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // This allows the window to be dragged
            this.DragMove();
        }

        private void AddButtons()
        {
            foreach (account acc in mainWindow.Accounts)
            {
                if(acc.IsRunning == true)
                { 
                Button btn = new Button();
                btn.Content = acc.Name;
                btn.Tag = acc;
                btn.Height = 100;
                btn.Click += (s, e) => { HandleButton(s, e); };
                DynamicButtonsArea.Items.Add(btn);

                }
            }
        }

        private void HandleButton(object sender, RoutedEventArgs e)
        {
            Button btn = (Button)sender;
            account acc = (account)btn.Tag;
            acc.Focus();
        }
    }
}
