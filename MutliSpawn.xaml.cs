using MiscUtils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using static EldenRingTool.ERProcess;

namespace EldenRingTool
{
    public partial class MutliSpawn : Window
    {
        public ObservableCollection<Item> AvailableItems { get; private set; }
        public ICollectionView AvailableItemsView { get; set; } // For filtering
        public ObservableCollection<Item> SelectedItems { get; set; }
        public ObservableCollection<Lookup> Infusions { get; private set; }
        public ObservableCollection<Lookup> Ashes { get; private set; }


        private Dictionary<string, List<string>> builds = new Dictionary<string, List<string>>();
        private string buildsFile = MainWindow.getBuildsFileAppData();
        private bool hasUnsavedChanges = false;

        public const string EMPTY_BUILD_NAME = "<New Profile>";

        ERProcess _process;


        public class Item
        {
            public string Name { get; set; }
            public string SearchName { get; set; }
            public uint Id { get; set; }
            public int Level { get; set; }
            public string InfusionName { get; set; }
            public string AshOfWarName { get; set; }
            public int Quantity { get; set; }
            public ERProcess.ItemCategory Category { get; set; }
        }

        public class Lookup
        {
            public string Name { get; set; }
            public uint Id { get; set; }
        }

        private Dictionary<string, List<string>> savedLists = new Dictionary<string, List<string>>();

        public MutliSpawn(ERProcess process)
        {
            _process = process;
            InitializeComponent();
            populate();
        }

        private void populate()
        {
            AvailableItems = new ObservableCollection<Item>(
                ItemDB.Items.Select(item => new Item
                {
                    Name = item.Item1,
                    SearchName = NormalizeForSearch(item.Item1),
                    Id = item.Item2,
                    Category = item.Item3
                })
            );

            Infusions = new ObservableCollection<Lookup>(
                ItemDB.Infusions.Select(item => new Lookup
                {
                    Name = item.Item1,
                    Id = item.Item2
                })
            );

            Ashes = new ObservableCollection<Lookup>(
                ItemDB.Ashes.Select(item => new Lookup
                {
                    Name = item.Item1,
                    Id = item.Item2
                })
            );

            SelectedItems = new ObservableCollection<Item>();
            SelectedItems.CollectionChanged += SelectedItems_CollectionChanged;

            AvailableItemsView = CollectionViewSource.GetDefaultView(AvailableItems);
            AvailableItemsView.Filter = FilterAvailableItems;


            LoadAllBuildsFromFile();

            DataContext = this;
        }

        private bool FilterAvailableItems(object obj)
        {
            if (obj is Item item)
            {
                if (SelectedItems.Contains(item))
                    return false;

                string filter = NormalizeForSearch(FilterBox?.Text);
                return string.IsNullOrEmpty(filter) || item.SearchName.Contains(filter);
            }
            return false;
        }

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filter = NormalizeForSearch(FilterBox.Text);

            AvailableItemsView.Filter = item =>
            {
                if (item is Item i)
                {
                    return i.SearchName.Contains(filter);
                }
                return false;
            };
        }

        //drops accents, punctuation and spacing so "reverse bladed" finds "Reverse-Bladed Sword"
        private static string NormalizeForSearch(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);

            foreach (char c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) { continue; }
                if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); }
            }

            return sb.ToString();
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            FilterBox.Text = string.Empty;
        }

        private void AvailableItemsListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            AddButton_Click(sender, e);
        }

        private void SelectedItemsListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            RemoveButton_Click(sender, e);
        }

        private void SelectedItems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            SpawnAllButton.IsEnabled = SaveButton.IsEnabled = SelectedItems.Any();
        }


        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableList.SelectedItem is Item selected)
            {
                var newItem = new Item
                {
                    Name = selected.Name,
                    Id = selected.Id,
                    Level = int.TryParse(comboLevel.Text, out int lvl) ? lvl : 0,
                    Quantity = int.TryParse(txtQuantity.Text, out int qty) ? qty : 1,
                    InfusionName = comboInfusion.SelectedItem is Lookup inf ? inf.Name : "Normal",
                    AshOfWarName = comboAsh.SelectedItem is Lookup ash ? ash.Name : "Default"
                };

                SelectedItems.Add(newItem);

                // Reset inputs to defaults
                txtQuantity.Text = "1";
                comboLevel.SelectedIndex = 0;
                comboInfusion.SelectedIndex = 0;
                comboAsh.SelectedIndex = 0;
            }

            hasUnsavedChanges = true;
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            var toMove = SelectedList.SelectedItems.Cast<Item>().ToList();
            foreach (var item in toMove)
            {
                SelectedItems.Remove(item);
            }

            hasUnsavedChanges = true;
        }

        private void QuickSpawn_Click(object sender, RoutedEventArgs e)
        {
            Item selectedItem = AvailableList.SelectedItem as Item;
            if (selectedItem == null)
            {
                MessageBox.Show("Please select an item from the list.");
                return;
            }

            try
            {
                uint level = 0;
                uint qty = 1;
                uint infusionId = 0;
                uint ashOfWarId = 0;

                uint.TryParse(comboLevel.Text, out level);
                uint.TryParse(txtQuantity.Text, out qty);

                var infusion = ItemDB.Infusions
                    .FirstOrDefault(x => x.Item1.Equals(comboInfusion.Text, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(infusion.Item1))
                    infusionId = infusion.Item2;

                var ash = ItemDB.Ashes
                    .FirstOrDefault(x => x.Item1.Equals(comboAsh.Text, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(ash.Item1))
                    ashOfWarId = ash.Item2;

                uint itemID = selectedItem.Id + level + infusionId;

                _process.spawnItem(itemID, qty, ashOfWarId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error spawning item: {ex.Message}");
            }
        }

        private void btnSpawnAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var selected in SelectedItems)
            {
                var item = ItemDB.Items.FirstOrDefault(x => x.Item1 == selected.Name);
                if (item == default) continue;

                var infusion = ItemDB.Infusions
                    .Select(x => new Lookup { Name = x.Item1, Id = x.Item2 })
                    .FirstOrDefault(x => x.Name == selected.InfusionName) ?? new Lookup { Name = "Normal", Id = 0 };

                var ash = ItemDB.Ashes
                    .Select(x => new Lookup { Name = x.Item1, Id = x.Item2 })
                    .FirstOrDefault(x => x.Name == selected.AshOfWarName) ?? new Lookup { Name = "Default", Id = 0 };

                uint itemID = item.Item2 + (uint)selected.Level + infusion.Id;
                uint qty = (uint)selected.Quantity;

                _process.spawnItem(itemID, qty, ash.Id);
            }
        }

        private void SaveBuild_Click(object sender, RoutedEventArgs e)
        {
            if (!SelectedItems.Any())
            {
                MessageBox.Show("Cannot save an empty profile. Please select at least one item.");
                return;
            }

            var existingProfiles = builds.Keys.ToList();

            var dialog = new SaveProfileDialog(existingProfiles, "Save Build", comboLoadBuild.Text)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                string buildName = dialog.SelectedProfileName;

                var itemsList = SelectedItems.Select(i =>
                    $"{i.Name.Replace("|", "||")}|{i.InfusionName.Replace("|", "||")}|{i.AshOfWarName.Replace("|", "||")}|{i.Level}|{i.Quantity}"
                ).ToList();

                builds[buildName] = itemsList;

                SaveAllBuildsToFile();
                hasUnsavedChanges = false;

                RefreshBuildList();

                comboLoadBuild.SelectedItem = buildName;

                MessageBox.Show($"Build \"{buildName}\" saved successfully.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SaveAllBuildsToFile()
        {
            var lines = builds
                .Where(kvp => kvp.Key != EMPTY_BUILD_NAME) 
                .Select(kvp =>
                kvp.Key.Replace("|", "||") + "|" + string.Join(";", kvp.Value)
            );
            File.WriteAllLines(buildsFile, lines);
        }

        private void DeleteBuild_Click(object sender, RoutedEventArgs e)
        {
            if (comboLoadBuild.SelectedItem == null)
            {
                MessageBox.Show("Please select a build from the dropdown to delete.");
                return;
            }

            string buildName = comboLoadBuild.SelectedItem.ToString();

            if (!builds.ContainsKey(buildName))
            {
                MessageBox.Show($"Build '{buildName}' not found.");
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete the build '{buildName}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                builds.Remove(buildName);

                RefreshBuildList();

                SaveAllBuildsToFile();
            }
        }

        private void LoadAllBuildsFromFile()
        {
            builds.Clear();
            if (!File.Exists(buildsFile)) return;

            foreach (var line in File.ReadAllLines(buildsFile))
            {
                var parts = line.Split('|');
                if (parts.Length < 2) continue;

                string buildName = parts[0].Replace("||", "|");
                string itemsString = string.Join("|", parts.Skip(1));

                var items = itemsString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s =>
                    {
                        var itemParts = s.Split(new[] { '|' }, StringSplitOptions.None);
                        return $"{itemParts[0].Replace("||", "|")}|{itemParts[1].Replace("||", "|")}|{itemParts[2].Replace("||", "|")}|{itemParts[3]}|{itemParts[4]}";
                    }).ToList();

                builds[buildName] = items;
            }

            builds = new Dictionary<string, List<string>> { { EMPTY_BUILD_NAME, new List<string>() } }
                        .Concat(builds)
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            RefreshBuildList();
        }

        private void AvailableItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AvailableList.SelectedItem is Item selected)
            {
                SingleSpawnButton.Content = "Spawn " + selected.Name;
                SingleSpawnButton.IsEnabled = true;

                int maxValue;
                switch (selected.Category)
                {
                    case ItemCategory.NONE:
                        maxValue = 0;
                        break;
                    case ItemCategory.SMITHING:
                        maxValue = 25;
                        break;
                    case ItemCategory.SOMBER:
                        maxValue = 10;
                        break;
                    default:
                        maxValue = 0;
                        break;
                }

                comboLevel.ItemsSource = Enumerable.Range(0, maxValue + 1);
                comboLevel.SelectedIndex = 0;
                comboLevel.IsEnabled = (selected.Category != ItemCategory.NONE); 
            }
            else
            {
                SingleSpawnButton.IsEnabled = false;
            }
        }

        private void ComboLoadBuild_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedBuild = comboLoadBuild.SelectedItem as string;
            
            DeleteButton.IsEnabled = (selectedBuild != EMPTY_BUILD_NAME);

            if (selectedBuild == EMPTY_BUILD_NAME)
            {
                SelectedItems.Clear();
                return;
            }

            if (hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Do you want to discard them and load the new build?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (result != MessageBoxResult.Yes)
                {
                    comboLoadBuild.SelectionChanged -= ComboLoadBuild_SelectionChanged;
                    comboLoadBuild.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null;
                    comboLoadBuild.SelectionChanged += ComboLoadBuild_SelectionChanged;
                    return;
                }
            }

            LoadBuild(selectedBuild);
            hasUnsavedChanges = false;
        }

        private void RefreshBuildList()
        {
            comboLoadBuild.ItemsSource = null;
            comboLoadBuild.ItemsSource = builds.Keys.ToList();
            comboLoadBuild.SelectedItem = EMPTY_BUILD_NAME;
        }

        private void LoadBuild(string buildName)
        {
            if (buildName is null || !builds.ContainsKey(buildName)) return;

            SelectedItems.Clear();

            foreach (var itemString in builds[buildName])
            {
                var parts = itemString.Split('|');
                if (parts.Length < 5) continue;

                SelectedItems.Add(new Item
                {
                    Name = parts[0],
                    InfusionName = parts[1],
                    AshOfWarName = parts[2],
                    Level = int.Parse(parts[3]),
                    Quantity = int.Parse(parts[4])
                });
            }

            hasUnsavedChanges = false;
        }

    }
}
