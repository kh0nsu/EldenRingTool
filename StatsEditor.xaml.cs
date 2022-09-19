using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace EldenRingTool
{
    public partial class StatsEditor : Window
    {
        List<(string, int)> _stats;
        Action<List<(string, int)>> _callback = null;
        List<TextBox> _boxes = new List<TextBox>();
        public StatsEditor(List<(string, int)> stats, Action<List<(string, int)>> callback)
        {
            InitializeComponent();
            _stats = stats;
            _callback = callback;
            lblExample.Visibility = Visibility.Hidden;
            txtExample.Visibility = Visibility.Hidden;
            for (int i = 0; i < stats.Count; i++)
            {
                statsGrid.RowDefinitions.Add(new RowDefinition());
                var lbl = new Label();
                lbl.Content = stats[i].Item1;
                statsGrid.Children.Add(lbl);
                Grid.SetRow(lbl, i);
                Grid.SetColumn(lbl, 0);
                var txt = new TextBox();
                txt.HorizontalAlignment = HorizontalAlignment.Stretch;
                txt.VerticalAlignment = VerticalAlignment.Center;
                txt.Text = stats[i].Item2.ToString();
                statsGrid.Children.Add(txt);
                Grid.SetRow(txt, i);
                Grid.SetColumn(txt, 1);
                _boxes.Add(txt);
            }
        }

        private void okClicked(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < _stats.Count; i++)
            {
                if (int.TryParse(_boxes[i].Text, out var stat))
                {
                    _stats[i] = (_stats[i].Item1, stat);
                }
            }
            _callback(_stats);
            Close();
        }
    }
}
