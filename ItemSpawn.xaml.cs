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
                var item = ItemDB.Items.Where(x => x.Item1.ToLower().Contains(txtItem.Text.ToLower())).FirstOrDefault();
                txtItem.Text = item.Item1;
                var level = uint.Parse(txtLevel.Text);
                var infus = ItemDB.Infusions.Where(x => x.Item1.ToLower().Contains(txtInfusion.Text.ToLower())).FirstOrDefault();
                txtInfusion.Text = infus.Item1;
                uint itemID = item.Item2 + level + infus.Item2;
                var ash = ItemDB.Ashes.Where(x => x.Item1.ToLower().Contains(txtAsh.Text.ToLower())).FirstOrDefault();
                txtAsh.Text = ash.Item1;
                _process.spawnItem(itemID, 1, ash.Item2);
            }
            catch
            {
            }
        }
    }
}
