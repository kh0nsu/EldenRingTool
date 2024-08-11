using System.Linq;
using System.Windows;

namespace EldenRingTool
{
    public partial class ItemSpawn : Window
    {
        ERProcess _process;
        public ItemSpawn(ERProcess process)
        {
            _process = process;
            InitializeComponent();
            populate();
            updateMatch();
        }

        void populate()
        {
            comboInfusion.Items.Clear();
            comboInfusion.ItemsSource = ItemDB.Infusions.Select(x => x.Item1).ToList();
            comboInfusion.SelectedIndex = 0;

            comboAsh.Items.Clear();
            comboAsh.ItemsSource = ItemDB.Ashes.Select(x => x.Item1).ToList();
            comboAsh.SelectedIndex = 0;
        }

        void updateMatch()
        {
            spawnItem(null, null);
            btnSpawn.Content = "Spawn " + matchingItem;
        }

        string matchingItem = "";

        private void spawnItem(object sender, RoutedEventArgs e)
        {
            try
            {
                if (txtItem.Text.Length < 1)
                {
                    matchingItem = "";
                    return;
                }
                var itemExact = ItemDB.Items.Where(x => x.Item1.ToLower().Equals(txtItem.Text.ToLower()));
                var itemStart = ItemDB.Items.Where(x => x.Item1.ToLower().StartsWith(txtItem.Text.ToLower()));
                var itemContain = ItemDB.Items.Where(x => x.Item1.ToLower().Contains(txtItem.Text.ToLower()));
                (string, uint) item = ("", 0);
                if (itemExact.Count() > 0)
                {
                    item = itemExact.First();
                }
                else if (itemStart.Count() > 0)
                {
                    item = itemStart.First();
                }
                else if (itemContain.Count() > 0)
                {
                    item = itemContain.First();
                }
                else
                {
                    matchingItem = "";
                    if (sender != null)
                    {
                        MessageBox.Show("Item not found");
                    }
                    return;
                }
                matchingItem = $"{item.Item1} [{item.Item2:X8}]";
                if (null == sender) { return; }
                txtItem.Text = item.Item1;
                uint level;
                if (!uint.TryParse(txtLevel.Text, out level)) { level = 0; }
                var infus = ItemDB.Infusions.Where(x => x.Item1.ToLower().Contains(comboInfusion.Text.ToLower())).FirstOrDefault();
                uint itemID = item.Item2 + level + infus.Item2;
                var ash = ItemDB.Ashes.Where(x => x.Item1.ToLower().Contains(comboAsh.Text.ToLower())).FirstOrDefault();
                uint qty;
                if (!uint.TryParse(txtQuantity.Text, out qty)) { qty = 1; }
                _process.spawnItem(itemID, qty, ash.Item2);
            }
            catch
            {
                if (sender != null)
                {
                    MessageBox.Show("Error");
                }
                return;
            }
        }

        private void showList(object sender, RoutedEventArgs e)
        {
            var sel = new Selection(ItemDB.Items.Select(x => x.Item1).ToList<object>(), (x) => { txtItem.Text = x as string; }, "Choose an item");
            sel.Owner = this;
            sel.Show();
        }

        private void txtItem_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            updateMatch();
        }
    }
}
