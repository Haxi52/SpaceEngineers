using System;
using System.Linq;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game;
using VRage;

namespace BuildQueue
{
    public class Program : MyGridProgram
    {

// -----------------------------Begin Copy Here-------------------------------------
        private static readonly Dictionary<string, string> ComponentMap = new Dictionary<string, string>() {
            { "SteelPlate", "SteelPlate" },
            { "Construction", "ConstructionComponent" },
            { "PowerCell", "PowerCell" },
            { "Computer", "ComputerComponent" },
            { "LargeTube", "LargeTube" },
            { "Motor", "MotorComponent" },
            { "Display", "Display" },
            { "MetalGrid", "MetalGrid" },
            { "InteriorPlate", "InteriorPlate" },
            { "SmallTube", "SmallTube" },
            { "RadioCommunication", "RadioCommunicationComponent" },
            { "BulletproofGlass ", "BulletproofGlass" },
            { "Girder", "GirderComponent" },
            { "Explosives", "ExplosivesComponent" },
            { "Detector", "DetectorComponent" },
            { "Medical", "MedicalComponent" },
            { "GravityGenerator", "GravityGeneratorComponent" },
            { "Superconductor", "Superconductor" },
            { "Thrust", "ThrustComponent" },
            { "Reactor", "ReactorComponent" },
            { "SolarCell", "SolarCell" },
        };

        private const string panelSectionName = "ShowInventory";
        private const string oreKeyName = "Ores";
        private const string componentKeyName = "Componenets";
        private const string ingotKeyName = "Ingots";

        private const string BlueprintType = "MyObjectBuilder_BlueprintDefinition";
        private const string CompItemType = "MyObjectBuilder_Component";
        private const string OreItemType = "MyObjectBuilder_Ore";
        private const string IngotItemType = "MyObjectBuilder_Ingot";
        private const string ComponentsConfigSectionName = "BuildQueueComponents";
        private const string IngotsConfigSectionName = "BuildQueueIngots";
        private const string OreConfigSectionName = "BuildQueueOres";
        private const string IgnoreAssemberConfig = "BuildQueueIgnore";
        private const int BatchAmount = 100;
        private readonly List<IMyInventory> TrackingInventories = new List<IMyInventory>();
        private readonly List<IMyAssembler> Assemblers = new List<IMyAssembler>();
        private Dictionary<ItemType, List<DisplayPanels>> Displays = new Dictionary<ItemType, List<DisplayPanels>>();
        private List<ItemTracker> ItemTrackers = new List<ItemTracker>();

        // reusable list of items;
        private List<MyInventoryItem> Items = new List<MyInventoryItem>();
        private List<MyProductionItem> AssemberQueue = new List<MyProductionItem>();

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.None;

            if (!GetConfig())
            {
                return;
            }

            GridTerminalSystem.GetBlocksOfType<IMyAssembler>(Assemblers, block => {

                if (MyIni.HasSection(block.CustomData, IgnoreAssemberConfig)) return false;
                if (!block.Enabled) return false;
                if (!block.IsFunctional) return false;
                // this assmeber is suppose to be working with others, so we can't queue for it specifically
                if (block.CooperativeMode) return false; 

                return true;
            });

            if (Assemblers.Count == 0)
            {
                Echo("No assembers found.");
            }

            var cargoContainers = new List<IMyCargoContainer>();
            GridTerminalSystem.GetBlocksOfType(cargoContainers, block => block.IsFunctional);
            foreach(var cargo in cargoContainers)
            {
                TrackingInventories.Add(cargo.GetInventory(0));
            }

            var refineries = new List<IMyRefinery>();
            GridTerminalSystem.GetBlocksOfType(refineries, block => block.IsFunctional && block.IsSameConstructAs(Me));
            foreach(var item in refineries)
            {
                TrackingInventories.Add(item.GetInventory(0));
                TrackingInventories.Add(item.GetInventory(1));
            }

            var assembers = new List<IMyAssembler>();
            GridTerminalSystem.GetBlocksOfType(assembers, block => block.IsFunctional && block.IsSameConstructAs(Me));
            foreach(var item in assembers)
            {
                TrackingInventories.Add(item.GetInventory(0));
                TrackingInventories.Add(item.GetInventory(1));
            }


            if (TrackingInventories.Count == 0)
            {
                Echo("No cargo containers found.");
                return;
            }

            GetPanelList();

            Runtime.UpdateFrequency = UpdateFrequency.Update100;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            UpdateCounts();
            WriteCountsToPanels(Displays[ItemType.Ore], ItemTrackers.Where(i => i.ItemType == ItemType.Ore));
            WriteCountsToPanels(Displays[ItemType.Componenet], ItemTrackers.Where(i => i.ItemType == ItemType.Componenet));
            WriteCountsToPanels(Displays[ItemType.Ingot], ItemTrackers.Where(i => i.ItemType == ItemType.Ingot));

            QueueAssemblers();
        }

        private void QueueAssemblers() 
        {
            foreach(var item in ItemTrackers.Where(i => i.ItemType == ItemType.Componenet && i.NeedsQueue()))
            {
                MyDefinitionId definition = VRage.Game.MyDefinitionId.Parse($"{BlueprintType}/{ComponentMap[item.IngameName]}");
                var count = item.TargetAmount - item.CurrentAmount;
                foreach(var assembler in Assemblers)
                {
                    if (!assembler.CanUseBlueprint(definition)) continue;
                    AssemberQueue.Clear();
                    assembler.GetQueue(AssemberQueue);
                    // if the assmeber is already building this item, skip it
                    if (AssemberQueue.Any(i => i.BlueprintId == definition)) continue;

                    assembler.AddQueueItem(definition, (MyFixedPoint)BatchAmount);
                    count -= BatchAmount;
                    if (count < 0) break;
                }
            }
        }

        private void UpdateCounts()
        {
            foreach(var item in this.ItemTrackers)
            {
                item.CurrentAmount = 0.0f;
            }

            // get current counts
            foreach(var inv in this.TrackingInventories)
            {
                Items.Clear();
                inv.GetItems(Items, item => item.Type.TypeId == CompItemType);
                foreach(var item in Items)
                {
                    var tracker = ItemTrackers.FirstOrDefault(i => i.IngameName == item.Type.SubtypeId && i.ItemType == ItemType.Componenet);
                    if (tracker == null) continue;
                    tracker.CurrentAmount += (float)item.Amount;
                }

                Items.Clear();
                inv.GetItems(Items, item => item.Type.TypeId == OreItemType);
                foreach(var item in Items)
                {
                    var tracker = ItemTrackers.FirstOrDefault(i => i.IngameName == item.Type.SubtypeId && i.ItemType == ItemType.Ore);
                    if (tracker == null) continue;
                    tracker.CurrentAmount += (float)item.Amount;
                }

                Items.Clear();
                inv.GetItems(Items, item => item.Type.TypeId == IngotItemType);
                foreach(var item in Items)
                {
                    var tracker = ItemTrackers.FirstOrDefault(i => i.IngameName == item.Type.SubtypeId && i.ItemType == ItemType.Ingot);
                    if (tracker == null) continue;
                    tracker.CurrentAmount += (float)item.Amount;
                }
            }
        }

        private void WriteCountsToPanels(List<DisplayPanels> panelList, IEnumerable<ItemTracker> items) 
        {
             var count = 0;
            var text =  new System.Text.StringBuilder();
            var panels = panelList.GetEnumerator();
            if (!panels.MoveNext()) return;
            foreach(var item in items.Where(i => i.TargetAmount > 0))
            {
                text.AppendLine(item.ToString());
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

        private bool GetConfig() 
        {
            var ini = new MyIni();
            if (!ini.TryParse(Me.CustomData))
            {
                Echo("Invalid configuration in PB.");
                return false;
            }

            if (!ini.ContainsSection(ComponentsConfigSectionName) &&
                !ini.ContainsSection(IngotsConfigSectionName) &&
                !ini.ContainsSection(OreConfigSectionName))
            {
                Echo($"Configuration must contain the section [{ComponentsConfigSectionName} or {IngotsConfigSectionName} or {OreConfigSectionName}].");
                return false;
            }

            var keys = new List<MyIniKey>();
            var count = 0;

            ini.GetKeys(ComponentsConfigSectionName, keys);
            foreach(var key in keys)
            {
                if (!ComponentMap.ContainsKey(key.Name))
                {
                    Echo("Invalid component type: " + key.Name + ".");
                    continue;
                }
                var parts = ini.Get(key).ToString(string.Empty).Split(',');
                if (parts.Length != 2 ||
                    !int.TryParse(parts[1], out count))
                {
                    Echo($"Invalid component config value {key.Name}={parts.FirstOrDefault()}");
                    continue;
                }

                ItemTrackers.Add(new ItemTracker(parts[0], key.Name, ItemType.Componenet, count));
            }

            if (ItemTrackers.Count == 0)
            {
                Echo("No components configured.");
            }

            ini.GetKeys(IngotsConfigSectionName, keys);
            foreach(var key in keys)
            {
                var parts = ini.Get(key).ToString(string.Empty).Split(',');
                if (parts.Length != 2 ||
                    !int.TryParse(parts[1], out count))
                {
                    Echo($"Invalid ingot config value {key.Name}={parts.FirstOrDefault()}");
                    continue;
                }

                ItemTrackers.Add(new ItemTracker(parts[0], key.Name, ItemType.Ingot, count));
            }

            ini.GetKeys(OreConfigSectionName, keys);
            foreach(var key in keys)
            {
                var parts = ini.Get(key).ToString(string.Empty).Split(',');
                if (parts.Length != 2 ||
                    !int.TryParse(parts[1], out count))
                {
                    Echo($"Invalid ore config value {key.Name}={parts.FirstOrDefault()}");
                    continue;
                }

                ItemTrackers.Add(new ItemTracker(parts[0], key.Name, ItemType.Ore, count));
            }

            if (ItemTrackers.Count == 0)
            {
                Echo("Nothing configured.");
                return false;
            }

            ItemTrackers.Sort();

            return true;
        }

        private void GetPanelList()
        {
            Displays.Add(ItemType.Ore, new List<DisplayPanels>());
            Displays.Add(ItemType.Ingot, new List<DisplayPanels>());
            Displays.Add(ItemType.Componenet, new List<DisplayPanels>());

            var ini = new MyIni();
            MyIniParseResult result;
            var panels = new List<IMyTextPanel>();
            GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(panels, panel => MyIni.HasSection(panel.CustomData, panelSectionName));

            foreach(var panel in panels)
            {
                if (!ini.TryParse(panel.CustomData, out result))
                {
                        Echo("Invalid configuration for panel " + panel.DisplayNameText);
                    continue;
                }

                if (ini.ContainsKey(panelSectionName, componentKeyName))
                {
                    var index = ini.Get(panelSectionName, componentKeyName).ToInt16();
                    var dPanel = Displays[ItemType.Componenet].FirstOrDefault(i => i.Index == index);
                    if (dPanel == null)
                    {
                        dPanel = new DisplayPanels()
                        {
                            Index = index,
                        };
                        Displays[ItemType.Componenet].Add(dPanel);
                    }
                    dPanel.Panels.Add(panel);
                }
                else if (ini.ContainsKey(panelSectionName, oreKeyName))
                {
                    var index = ini.Get(panelSectionName, oreKeyName).ToInt16();
                    var dPanel = Displays[ItemType.Ore].FirstOrDefault(i => i.Index == index);
                    if (dPanel == null)
                    {
                        dPanel = new DisplayPanels()
                        {
                            Index = index,
                        };
                        Displays[ItemType.Ore].Add(dPanel);
                    }
                    dPanel.Panels.Add(panel);
                }
                else if (ini.ContainsKey(panelSectionName, ingotKeyName))
                {
                    var index = ini.Get(panelSectionName, ingotKeyName).ToInt16();
                    var dPanel = Displays[ItemType.Ingot].FirstOrDefault(i => i.Index == index);
                    if (dPanel == null)
                    {
                        dPanel = new DisplayPanels()
                        {
                            Index = index,
                        };
                        Displays[ItemType.Ingot].Add(dPanel);
                    }
                    dPanel.Panels.Add(panel);
                }
            }

            if (Displays[ItemType.Componenet].Count == 0)
            {
                Echo("No component panels configured.");
            }
            else
            {
                Displays[ItemType.Componenet].Sort();
            }

            if (Displays[ItemType.Ore].Count == 0)
            {
                Echo("No ore panels configured.");
            }
            else
            {
                Displays[ItemType.Ore].Sort();
            }

            if (Displays[ItemType.Ingot].Count == 0)
            {
                Echo("No ingot panels configured.");
            }
            else
            {
                Displays[ItemType.Ingot].Sort();
            }
        }


        private class ItemTracker : IComparable<ItemTracker>
        {
            private const int progressFillSize = 20;

            public string DisplayName { get; }
            public string IngameName { get; }
            public ItemType ItemType { get; }
            public float TargetAmount { get; }
            public float CurrentAmount { get; set;}
            public ItemTracker(string displayName, string ingameName, ItemType itemType, float targetAmount)
            {
                DisplayName = displayName;
                IngameName = ingameName;
                ItemType = itemType;
                TargetAmount = targetAmount;
            }

            internal bool NeedsQueue()
            {
                return TargetAmount > CurrentAmount;
            }

            private string GetProgressBar()
            {
                var percent = CurrentAmount/TargetAmount;
                var fillChars = (int)(percent * progressFillSize);
                var leftSide = new string('!', Math.Min(progressFillSize, Math.Max(0, fillChars)));
                var rightSide = new string(' ', Math.Min(progressFillSize, Math.Max(0, progressFillSize - fillChars)));
                return $"[{leftSide}{rightSide}]";
            }

            private string GetFormatAmount(float value)
            {
                if (value >= 1000000)
                {
                    return (value/1000000).ToString() + "M";
                }
                else if (value >= 1000)
                {
                    return (value/1000).ToString() + "K";
                }
                else
                {
                    return value.ToString("0");
                }
            }

            public override string ToString()
            {
                return $"{DisplayName} {GetProgressBar()} {GetFormatAmount(CurrentAmount)}/{GetFormatAmount(TargetAmount)}";
            }

            public int CompareTo(ItemTracker other)
            {
                return this.DisplayName.CompareTo(other.DisplayName);
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

        private enum ItemType{
            Ore,
            Ingot,
            Componenet,
            Ammo,
            Weapon,
            Tool,
        }
        
// ------------------------------End Copy Here--------------------------------------
    }
}
/*

-- These sections go in the PB that runs the script


[BuildQueueComponents]
SteelPlate=Plates,20000
Construction=Const,10000
PowerCell=Power,2000
Computer=Computer,5000
LargeTube=L Tube,5000
Motor=Motor,10000
Display=Disp,1000
MetalGrid=Grid,5000
InteriorPlate=Int.Plate,5000
SmallTube=S Tube,5000
RadioCommunication=Radio,100
BulletproofGlass=Glass,2000
Girder=Girder,2000
Explosives=Explo,0
Detector=Detect,100
Medical=Med,100
GravityGenerator=Grav,100
Superconductor=Super,1000
Thrust=Thrust,10000
Reactor=React,4000
SolarCell=Solar,2000

[BuildQueueIngots]
Iron=Iron,100000
Nickel=Nickel,50000
Silicon=Silicon,50000
Cobalt=Cobalt,25000
Gold=Gold,25000
Silver=Silver,25000
Platinum=Plat,25000
Uranium=Uranium,10000
Stone=Stone,0

[BuildQueueOres]
Scrap=Scrap,0
Ice=Ice,100000
Iron=Iron,100000
Nickel=Nickel,50000
Silicon=Silicon,50000
Cobalt=Cobalt,25000
Gold=Gold,25000
Silver=Silver,25000
Platinum=Plat,25000
Uranium=Uranium,10000
Stone=Stone,0

-- Place these in the appropreate panels
[ShowInventory]
Ores=1


[ShowInventory]
Componenets=1


[ShowInventory]
Ingots=1

 */
    