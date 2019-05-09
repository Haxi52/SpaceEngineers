using System;
using System.Linq;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace ShowInventory
{
    public class Program : Sandbox.ModAPI.Ingame.MyGridProgram
    {
// -----------------------------Begin Copy Here-------------------------------------

        private const string panelSectionName = "ShowInventory";
        private const string oreKeyName = "Ores";
        private const string componentKeyName = "Componenets";
        private const string ingotKeyName = "Ingots";

        private List<InventoryManager> Inventories = new List<InventoryManager>();
        private List<DisplayItem> Ores = new List<DisplayItem>();
        private List<DisplayItem> Componenets = new List<DisplayItem>();
        private List<DisplayItem> Ingots = new List<DisplayItem>();
        private List<DisplayPanels> OrePanels = new List<DisplayPanels>();
        private List<DisplayPanels> ComponentPanels = new List<DisplayPanels>();
        private List<DisplayPanels> IngotsPanels = new List<DisplayPanels>();

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            Setup();
            // delete me
            var rotor = GridTerminalSystem.GetBlockWithName("Builder Rotor 2") as IMyMotorStator;
            rotor.TargetVelocityRPM = 0.1f;
            //
        }


        private void Setup()
        {
            bool isValid = true;

            Inventories.Clear();
            var allBlocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(allBlocks, block => block.HasInventory);

            foreach(var block in allBlocks)
            {
                for(var i = 0; i < block.InventoryCount; i++)
                {
                    Inventories.Add(new InventoryManager(block.GetInventory(i)));
                }
            }

            var ini = new MyIni();
            MyIniParseResult result;
            var panels = new List<IMyTextPanel>();
            GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(panels, panel => MyIni.HasSection(panel.CustomData, panelSectionName));
            foreach(var panel in panels)
            {
                if (!ini.TryParse(panel.CustomData, out result))
                {
                    Echo("Invalid configuration for panel " + panel.DisplayNameText);
                    isValid = false;
                    continue;
                }

                if (ini.ContainsKey(panelSectionName, componentKeyName))
                {
                    var index = ini.Get(panelSectionName, componentKeyName).ToInt16();
                    var dPanel = ComponentPanels.FirstOrDefault(i => i.Index == index);
                    if (dPanel == null)
                    {
                        dPanel = new DisplayPanels()
                        {
                            Index = index,
                        };
                        ComponentPanels.Add(dPanel);
                    }
                    dPanel.Panels.Add(panel);
                }
                else if (ini.ContainsKey(panelSectionName, oreKeyName))
                {
                    var index = ini.Get(panelSectionName, oreKeyName).ToInt16();
                    var dPanel = OrePanels.FirstOrDefault(i => i.Index == index);
                    if (dPanel == null)
                    {
                        dPanel = new DisplayPanels()
                        {
                            Index = index,
                        };
                        OrePanels.Add(dPanel);
                    }
                    dPanel.Panels.Add(panel);
                }
                else if (ini.ContainsKey(panelSectionName, ingotKeyName))
                {
                    var index = ini.Get(panelSectionName, ingotKeyName).ToInt16();
                    var dPanel = IngotsPanels.FirstOrDefault(i => i.Index == index);
                    if (dPanel == null)
                    {
                        dPanel = new DisplayPanels()
                        {
                            Index = index,
                        };
                        IngotsPanels.Add(dPanel);
                    }
                    dPanel.Panels.Add(panel);
                }

                panel.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
            }

            if (ComponentPanels.Count + OrePanels.Count + IngotsPanels.Count == 0)
            {
                Echo("No valid panels found for display");
                isValid = false;
            }

            if (!isValid)
            {
                Runtime.UpdateFrequency = UpdateFrequency.None;
            }

            ComponentPanels.Sort();
            IngotsPanels.Sort();
            OrePanels.Sort();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            Echo("Running");

            ClearCounts();
            
            SumAndSortInventories();

            WriteInventoryToPanels();
        }

        private void ClearCounts() 
        {
            foreach(var item in Ores)
            {
                item.Clear();
            }
            foreach(var item in Ingots)
            {
                item.Clear();
            }
            foreach(var item in Componenets)
            {
                item.Clear();
            }
        }

        private void SumAndSortInventories() 
        {
            foreach(var inventory in Inventories)
            {
                if (OrePanels.Count > 0)
                {
                    inventory.GetOres((name, amount) => {
                        var item = Ores.FirstOrDefault(i => name.Equals(i.Name));
                        if (item == null)
                        {
                            item = new DisplayItem(name, 0);
                            Ores.Add(item);
                        }
                        item.Add((float)amount);
                    });
                }

                if (IngotsPanels.Count > 0)
                {
                    inventory.GetIngots((name, amount) => {
                        var item = Ingots.FirstOrDefault(i => name.Equals(i.Name));
                        if (item == null)
                        {
                            item = new DisplayItem(name, 0);
                            Ingots.Add(item);
                        }
                        item.Add((float)amount);
                    });
                }

                if (ComponentPanels.Count > 0)
                {
                    inventory.GetComponents((name, amount) => {
                        var item = Componenets.FirstOrDefault(i => name.Equals(i.Name));
                        if (item == null)
                        {
                            item = new DisplayItem(name, 0);
                            Componenets.Add(item);
                        }
                        item.Add((float)amount);
                    });
                }
            }

            Ores.Sort();
            Ingots.Sort();
            Componenets.Sort();
        }

        private void WriteInventoryToPanels() 
        {
            WriteInventoryToPanels(OrePanels, Ores);
            WriteInventoryToPanels(ComponentPanels, Componenets);
            WriteInventoryToPanels(IngotsPanels, Ingots);
        }

        private static void WriteInventoryToPanels(List<DisplayPanels> panelList, List<DisplayItem> items)
        {
            var count = 0;
            var text =  new System.Text.StringBuilder();
            var panels = panelList.GetEnumerator();
            if (!panels.MoveNext()) return;
            foreach(var item in items.Where(i => i.Amount > 0))
            {
                text.AppendLine(item.GetDisplay());
                if (++count > 16)
                {
                    count = 0;
                    panels.Current.Write(text);
                    text.Clear();
                    if (!panels.MoveNext()) return;
                }
            }
            panels.Current.Write(text);
        }

        private class InventoryManager
        {
            private static readonly string OreType = "MyObjectBuilder_Ore";
            private static readonly string IngotType = "MyObjectBuilder_Ingot";
            private static readonly string ComponentType = "MyObjectBuilder_Component";
            private readonly IMyInventory inventory;
            private List<MyInventoryItem> items = new List<MyInventoryItem>();
            public InventoryManager(IMyInventory inventory)
            {
                this.inventory = inventory;
            }

            public void GetOres(Action<string, MyFixedPoint> ores) 
            {
                items.Clear();
                inventory.GetItems(items, item => OreType.Equals(item.Type.TypeId));
                foreach(var item in items)
                {
                    ores(item.Type.SubtypeId, item.Amount);
                }
            }

            public void GetIngots(Action<string, MyFixedPoint> ingots) 
            {
                items.Clear();
                inventory.GetItems(items, item => IngotType.Equals(item.Type.TypeId));
                foreach(var item in items)
                {
                    ingots(item.Type.SubtypeId, item.Amount);
                }
            }

            public void GetComponents(Action<string, MyFixedPoint> comps) 
            {
                items.Clear();
                inventory.GetItems(items, item => ComponentType.Equals(item.Type.TypeId));
                foreach(var item in items)
                {
                    comps(item.Type.SubtypeId, item.Amount);
                }
            }

        }

        private class DisplayItem : IComparable<DisplayItem>
        {
            public string Name { get; }
            public float Amount { get; private set; }

            public DisplayItem(string name, float amount)
            {
                Name = name;
                Amount = amount;
            }

            public void Add(float amount)
            {
                Amount += amount;
            }

            public void Clear() 
            {
                Amount = 0.0f;
            }

            public string GetDisplay() 
            {
                return string.Format("{0}: {1:0.0#}", Name, Amount);
            }

            public int CompareTo(DisplayItem other)
            {
                return this.Amount.CompareTo(other.Amount);
            }
        }

        private class DisplayPanels : IComparable<DisplayPanels>
        {
            public int Index {get;set;}
            public List<IMyTextPanel> Panels {get;set;} = new List<IMyTextPanel>();

            public void Write(System.Text.StringBuilder text)
            {
                foreach(var panel in Panels)
                {
                    panel.WriteText(text);
                }
            }

            public int CompareTo(DisplayPanels other)
            {
                return this.Index.CompareTo(other.Index);
            }
        }
// ------------------------------End Copy Here--------------------------------------
    }
}