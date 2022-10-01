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
        }

        private void spawnItem(object sender, RoutedEventArgs e)
        {
            try
            {
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
                    MessageBox.Show("Item not found");
                    return;
                }
                txtItem.Text = item.Item1;
                uint level;
                if (!uint.TryParse(txtLevel.Text, out level)) { level = 0; }
                var infus = ItemDB.Infusions.Where(x => x.Item1.ToLower().Contains(txtInfusion.Text.ToLower())).FirstOrDefault();
                txtInfusion.Text = infus.Item1;
                uint itemID = item.Item2 + level + infus.Item2;
                var ash = ItemDB.Ashes.Where(x => x.Item1.ToLower().Contains(txtAsh.Text.ToLower())).FirstOrDefault();
                txtAsh.Text = ash.Item1;
                uint qty;
                if (!uint.TryParse(txtQuantity.Text, out qty)) { qty = 1; }
                _process.spawnItem(itemID, qty, ash.Item2);
            }
            catch
            {
                MessageBox.Show("Error");
                return;
            }
        }

        private void showList(object sender, RoutedEventArgs e)
        {
            var sel = new Selection(ItemDB.Items.Select(x => x.Item1).ToList<object>(), (x) => { txtItem.Text = x as string; }, "Choose an item");
            sel.Owner = this;
            sel.Show();
        }
    }
}
