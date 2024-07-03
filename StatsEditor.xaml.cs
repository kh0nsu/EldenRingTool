using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
                _boxes.Add(txt);

                var decButton = new Button();
                decButton.Height = 18;
                decButton.HorizontalAlignment = HorizontalAlignment.Stretch;
                decButton.IsTabStop = false;
                decButton.Content = "-";
                decButton.Click += (sender, e) => Button_DecreaseStat(txt);

                var incButton = new Button();
                incButton.Height = 18;
                incButton.HorizontalAlignment = HorizontalAlignment.Stretch;
                incButton.IsTabStop = false;
                incButton.Content = "+";
                incButton.Click += (sender, e) => Button_IncreaseStat(txt);

                Grid.SetRow(decButton, i);
                Grid.SetColumn(decButton, 1);
                Grid.SetRow(txt, i);
                Grid.SetColumn(txt, 2);
                Grid.SetRow(incButton, i);
                Grid.SetColumn(incButton, 3);

                statsGrid.Children.Add(txt);
                statsGrid.Children.Add(decButton);
                statsGrid.Children.Add(incButton);
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

        private void TextBox_GotKeyboardFocus(object sender, RoutedEventArgs e)
        {
            (sender as TextBox)?.SelectAll();
        }

        private void Button_DecreaseStat(TextBox txt)
        {
            if (int.TryParse(txt.Text, out int value)) { 
                txt.Text = (--value).ToString();
            }
        }

        private void Button_IncreaseStat(TextBox txt)
        {
            if (int.TryParse(txt.Text, out int value)) { 
                txt.Text = (++value).ToString();
            }
        }
    }
}
