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

namespace EldenRingTool
{
    /// <summary>
    /// Interaction logic for Selection.xaml
    /// </summary>
    public partial class Selection : Window
    {
        Action<object> _callback;
        public Selection(List<object> items, Action<object> callback, string name = null)
        {
            _callback = callback;
            InitializeComponent();
            if (!string.IsNullOrWhiteSpace(name)) { Title = name; }
            listBox.ItemsSource = items;
        }

        private void okClick(object sender, RoutedEventArgs e)
        {
            _callback(listBox.SelectedItem);
            Close();
        }
    }
}
