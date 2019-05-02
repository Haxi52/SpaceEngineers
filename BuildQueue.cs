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

        private static readonly string BlueprintType = "MyObjectBuilder_BlueprintDefinition";
        private static readonly string CompItemType = "MyObjectBuilder_Component";
        private static readonly string ConfigSectionName = "BuildQueue";
        private static readonly string IgnoreAssemberConfig = "BuildQueueIgnore";
        private static readonly string ProductionDisplayName = "ProductionDisplay";
        private static readonly int BatchAmount = 100;
        private readonly List<IMyInventory> TrackingInventories = new List<IMyInventory>();
        private readonly List<IMyAssembler> Assemblers = new List<IMyAssembler>();
        private readonly Dictionary<string, int> TargetCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, int> CurrentCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, int> ProductionCounts = new Dictionary<string, int>();

        // reusable list of items;
        private List<MyInventoryItem> Items = new List<MyInventoryItem>();
        private List<MyProductionItem> AssemberQueue = new List<MyProductionItem>();
        private List<IMyTextPanel> ProductionDisplays = new List<IMyTextPanel>();
        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Once;

            var ini = new MyIni();
            if (!ini.TryParse(Me.CustomData))
            {
                Echo("Invalid configuration in PB.");
                return;
            }

            if (!ini.ContainsSection(ConfigSectionName))
            {
                Echo("Configuration must contain the section " + ConfigSectionName + ".");
                return;
            }

            var keys = new List<MyIniKey>();
            ini.GetKeys(ConfigSectionName, keys);

            foreach(var key in keys)
            {
                if (!ComponentMap.ContainsKey(key.Name))
                {
                    Echo("Invalid component type: " + key.Name + ".");
                    continue;
                }
                var count = ini.Get(key).ToInt32();
                if (count <= 0)
                {
                    // allow compontents to be zero count = ignore me
                    continue;
                }
                TargetCounts.Add(key.Name, count);
                CurrentCounts.Add(key.Name, 0);
                ProductionCounts.Add(key.Name, 0);
            }

            if (TargetCounts.Count == 0)
            {
                Echo("No components configured.");
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
                return;
            }

            var cargoContainers = new List<IMyCargoContainer>();
            GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(cargoContainers, block => block.IsFunctional);
            foreach(var cargo in cargoContainers)
            {
                TrackingInventories.Add(cargo.GetInventory(0));
            }

            if (TrackingInventories.Count == 0)
            {
                Echo("No cargo containers found.");
                return;
            }

            GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(ProductionDisplays, block => MyIni.HasSection(block.CustomData, ProductionDisplayName));

            Runtime.UpdateFrequency = UpdateFrequency.Update100;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            // clear current counts;
            foreach(var key in CurrentCounts.Keys.ToArray())
            {
                CurrentCounts[key] = 0;
                ProductionCounts[key] = 0;
            }

            foreach(var assembler in Assemblers)
            {
                AssemberQueue.Clear();
                assembler.GetQueue(AssemberQueue);
                foreach(var item in AssemberQueue)
                {
                    var map = ComponentMap.FirstOrDefault(i => i.Value == item.BlueprintId.SubtypeName);
                    if (map.Key == null) continue;
                    ProductionCounts[map.Key] += (int)item.Amount;
                }
            }

            // get current counts
            foreach(var inv in this.TrackingInventories)
            {
                Items.Clear();
                inv.GetItems(Items, item => item.Type.TypeId == CompItemType);
                foreach(var item in Items)
                {
                    if (CurrentCounts.ContainsKey(item.Type.SubtypeId))
                    {
                        CurrentCounts[item.Type.SubtypeId] += (int)item.Amount;
                    }
                }
            }
       
            // which ones do we need to queue for
            foreach(var item in TargetCounts)
            {
                var currentCount = CurrentCounts[item.Key];
                if (item.Value > currentCount)
                {
                    MyDefinitionId definition = VRage.Game.MyDefinitionId.Parse($"{BlueprintType}/{ComponentMap[item.Key]}");

                    foreach(var assembler in Assemblers)
                    {
                        if (!assembler.CanUseBlueprint(definition)) continue;
                        AssemberQueue.Clear();
                        assembler.GetQueue(AssemberQueue);
                        // if the assmeber is already building this item, skip it
                        if (AssemberQueue.Any(i => i.BlueprintId == definition)) break;

                        assembler.AddQueueItem(definition, (MyFixedPoint)BatchAmount);
                        ProductionCounts[item.Key] += BatchAmount;
                    }
                }
            }

            if (ProductionDisplays.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            foreach(var item in ProductionCounts)
            {
                if (item.Value > 0)
                {
                    sb.AppendLine($"{item.Key}: {item.Value}");
                }
            }

            foreach(var item in ProductionDisplays)
            {
                item.WriteText(sb.ToString());
            }
        }

        
        
// ------------------------------End Copy Here--------------------------------------
    }
}
/*
[BuildQueue]
SteelPlate=20000
Construction=10000
PowerCell=2000
Computer=2000
LargeTube=2000
Motor=5000
Display=1000
MetalGrid=2000
InteriorPlate=2000
SmallTube=2000
RadioCommunication=100
BulletproofGlass=2000
Girder=1000
Explosives=0
Detector=40
Medical=0
GravityGenerator=0
Superconductor=1000
Thrust=5000
Reactor=2000
SolarCell=2000
 */
    