using System;
using System.Linq;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage;

namespace MinerAssist
{
    public class Program : MyGridProgram
    {
// -----------------------------Begin Copy Here-------------------------------------

/*
[MinerAssist]
EjectorGroupName=OreEjectors
Stone=0
Ice=0
 */


        private const string SetupSectionName = "MinerAssist";
        private const string DefaultOreEjectorGroupName = "OreEjectors";
        private const string OreType = "MyObjectBuilder_Ore";
        private readonly Dictionary<string, int> EjectingOres = new Dictionary<string, int>();

        private List<IMyInventory> minerInventories = new List<IMyInventory>();
        private List<IMyShipConnector> minerEjectors = new List<IMyShipConnector>();

        // reusable lists
        private List<MyInventoryItem> items = new List<MyInventoryItem>();
        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.None;

            var ejectorGroupName = DefaultOreEjectorGroupName;
            var ini = new MyIni();
            MyIniParseResult result;
            if (ini.TryParse(Me.CustomData, out result))
            {
                var key = ini.Get(SetupSectionName, "EjectorGroupName");
                var value = string.Empty;
                if (key.TryGetString(out value))
                {
                    ejectorGroupName = value;
                }

                var keys = new List<MyIniKey>();
                ini.GetKeys(SetupSectionName, keys);
                foreach(var item in keys)
                {
                    var amount = 0;
                    if (ini.Get(item.Section, item.Name).TryGetInt32(out amount))
                    {
                        EjectingOres.Add(item.Name, amount);
                    }
                }
            }
            else
            {
                Echo("Invalid config.");
                return;
            }

            if (EjectingOres.Count == 0)
            {
                Echo("No ores configured.");
                return;
            }

            var ejectorGroup = GridTerminalSystem.GetBlockGroupWithName(ejectorGroupName);
            if (ejectorGroup == null)
            {
                Echo($"Ejector group {ejectorGroupName} does not exist.");
                return;
            }
            
            var ejectorBlocks = new List<IMyShipConnector>();
            ejectorGroup.GetBlocksOfType(ejectorBlocks, block => block.IsFunctional);

            foreach(var item in ejectorBlocks)
            {
                minerEjectors.Add(item);
            }

            if (minerEjectors.Count == 0)
            {
                Echo("No ejectors (from group) found.");
                return;
            }

            var terminalBlocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocks(terminalBlocks);

            foreach(var item in terminalBlocks)
            {
                if (!item.IsFunctional) continue;
                if (!item.HasInventory) continue;
                if (!item.IsSameConstructAs(Me)) continue;
                if (ejectorBlocks.Contains(item)) continue;

                minerInventories.Add(item.GetInventory());
            }

            if (minerInventories.Count == 0)
            {
                Echo("No inventories found.");
                return;
            }

        }

        public void Main(string argument, UpdateType updateSource)
        {

            if (!string.IsNullOrWhiteSpace(argument))
            {
                if ("start".Equals(argument, StringComparison.InvariantCultureIgnoreCase))
                {
                    Runtime.UpdateFrequency = UpdateFrequency.Update10;
                }
                else
                {
                    Runtime.UpdateFrequency = UpdateFrequency.None;
                    foreach(var ejector in minerEjectors)
                    {
                        ejector.ThrowOut = false;
                    }
                    return;
                }
            }

            Echo("Running.");

            var ejectors = minerEjectors.GetEnumerator();
            if (!ejectors.MoveNext()) return;
            ejectors.Current.ThrowOut = false;
            var currentEjector = ejectors.Current.GetInventory();

            foreach(var inventory in minerInventories)
            {
                items.Clear();
                inventory.GetItems(items, i => i.Type.TypeId == OreType && EjectingOres.ContainsKey(i.Type.SubtypeId));
                foreach(var item in items)
                {
                    var limit = (float)EjectingOres[item.Type.SubtypeId];
                    var amountToEject = ((float)item.Amount - limit);
                    while(amountToEject > 0)
                    {
                        while(currentEjector.IsFull)
                        {
                            ejectors.Current.ThrowOut = true;
                            if (!ejectors.MoveNext()) return;
                            currentEjector = ejectors.Current.GetInventory();
                        }

                        var massBeforeTransfer = currentEjector.CurrentMass;
                        if (currentEjector.TransferItemFrom(inventory, item, (MyFixedPoint)amountToEject))
                        {
                            amountToEject -= (float)(currentEjector.CurrentMass - massBeforeTransfer);
                        }

                        ejectors.Current.ThrowOut = true;
                        if (!ejectors.MoveNext()) return;
                        currentEjector = ejectors.Current.GetInventory();
                    }
                }
            }

            while(ejectors.MoveNext())
            {
                ejectors.Current.ThrowOut = false;
            }
        }
        
// ------------------------------End Copy Here--------------------------------------
    }
}