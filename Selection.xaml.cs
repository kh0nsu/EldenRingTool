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

            SetMaxHeight();//try and prevent OK button clipping off screeen
            DpiChanged += OnDpiChanged;
        }

        private void OnDpiChanged(object sender, DpiChangedEventArgs e)
        {
            SetMaxHeight();
        }

        private void SetMaxHeight()
        {
            MaxHeight = SystemParameters.PrimaryScreenHeight * 0.9;
        }

        private void okClick(object sender, RoutedEventArgs e)
        {
            _callback(listBox.SelectedItem);
            Close();
        }
    }
}
