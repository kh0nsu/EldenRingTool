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
                var level = uint.Parse(txtLevel.Text);
                var infus = ItemDB.Infusions.Where(x => x.Item1.ToLower().Contains(txtInfusion.Text.ToLower())).FirstOrDefault();
                txtInfusion.Text = infus.Item1;
                uint itemID = item.Item2 + level + infus.Item2;
                var ash = ItemDB.Ashes.Where(x => x.Item1.ToLower().Contains(txtAsh.Text.ToLower())).FirstOrDefault();
                txtAsh.Text = ash.Item1;
                uint qty = uint.Parse(txtQuantity.Text);
                _process.spawnItem(itemID, qty, ash.Item2);
            }
            catch
            {
            }
        }
    }
}
