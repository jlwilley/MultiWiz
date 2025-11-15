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
using NHotkey;
using NHotkey.Wpf;

namespace MultiWiz
{

    /// <summary>
    /// Interaction logic for Switcher.xaml
    /// </summary>
    public partial class Switcher : Window
    {
        private MainWindow mainWindow;
        private LinkedList<Button> buttons;
        private Button currentButton;

        public Switcher(MainWindow mw)
        {
            mainWindow = mw;
            InitializeComponent();
            buttons = new LinkedList<Button>();
            AddButtons();
            HotkeyManager.Current.AddOrReplace("MoveUp", Key.W, ModifierKeys.Control, MoveUp);
            HotkeyManager.Current.AddOrReplace("MoveDown", Key.S, ModifierKeys.Control, MoveDown);

            // Apply opacity from settings
            RootBorder.Opacity = mainWindow.SwitcherOpacity;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            mainWindow.Show();
            this.Close();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Get the width and height of the primary screen
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;

            // Set the window's Left property to position it on the right side of the screen
            this.Left = screenWidth - this.Width;

            // Set the window's Top property to position it 1/3 down from the top
            this.Top = screenHeight / 3;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // This allows the window to be dragged
            this.DragMove();
        }

        private void AddButtons()
        {
            foreach (Account acc in mainWindow.Accounts)
            {
                if(acc.IsRunning == true)
                {
                Button btn = new Button();
                btn.Content = acc.Name;
                btn.Tag = acc;
                btn.Height = 56;
                btn.Style = (Style)FindResource("AccountCardStyle");
                    btn.Click += (s, e) => { HandleButton(s, e); };
                    DynamicButtonsArea.Items.Add(btn);
                    buttons.AddLast(btn);
                }
            }
            if (buttons != null && buttons.Count > 0)
            {
                currentButton = buttons.First.Value;
                Mark(currentButton);
            }
        }

        private void HandleButton(object sender, RoutedEventArgs e)
        {
            Button btn = (Button)sender;
            Mark(btn);
            Account acc = (Account)btn.Tag;
            acc.Focus();
        }

        private void Mark(Button b)
        {
            // Apply unmarked style to all buttons
            foreach( Button btn in buttons)
            {
                if (btn != b)
                {
                    btn.Style = (Style)FindResource("AccountCardStyle");
                }
            }

            // Apply marked style to the selected button
            // The check icon visibility is handled by the style's template
            b.Style = (Style)FindResource("MarkedAccountCardStyle");
            currentButton = b;
        }

        private void MoveUp(object sender, HotkeyEventArgs e)
        {
            if (currentButton != null && buttons != null)
            {
                LinkedListNode<Button> currentNode = buttons.Find(currentButton);
                if(currentNode != null && currentNode.Previous != null)
                {
                    currentNode.Previous.Value.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                
            }
            e.Handled = true;
        }

        private void MoveDown(object sender, HotkeyEventArgs e)
        {
            if (currentButton != null && buttons != null)
            {
                LinkedListNode<Button> currentNode = buttons.Find(currentButton);
                if (currentNode != null && currentNode.Next != null)
                {
                    currentNode.Next.Value.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }

            }
            e.Handled = true;
        }
    }
}
