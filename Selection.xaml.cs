using System;
using System.Collections.Generic;
using System.Windows;

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
