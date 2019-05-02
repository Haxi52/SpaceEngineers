using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;

public class Program : Sandbox.ModAPI.Ingame.MyGridProgram
{
        // ----------------------------------------


    bool SetupValid;
    List<IMyCargoContainer> OreContainers;
    List<IMyCargoContainer> CompContainers;
    List<IMyCargoContainer> AllContainers;
    List<IMyAssembler> Assemblers;

    public Program()
    {

        Runtime.UpdateFrequency = UpdateFrequency.Update100;

        OreContainers = new List<IMyCargoContainer>();
        CompContainers = new List<IMyCargoContainer>();
        AllContainers = new List<IMyCargoContainer>();
        var tempAllContainers = new List<IMyCargoContainer>();
        var containers = new List<IMyCargoContainer>();
        GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(containers);

        foreach(var container in containers)
        {
            if (!container.IsWorking) continue;
            if (!container.IsSameConstructAs(Me)) continue;
            
            if (container.DisplayNameText.StartsWith("Ore")) 
            {
                OreContainers.Add(container);
            }
            else if (container.DisplayNameText.StartsWith("Comp"))
            {
                CompContainers.Add(container);
            }
            tempAllContainers.Add(container);
        }    
        Assemblers = new List<IMyAssembler>();
        GridTerminalSystem.GetBlocksOfType<IMyAssembler>(Assemblers);

        // Validate setup
        SetupValid = true;
        
        // First make sure the containers are connected to at least one other container.
        var checkOreContainers = new List<IMyCargoContainer>(OreContainers);
        var checkCompContainers = new List<IMyCargoContainer>(CompContainers);

        foreach(var oreContainer in checkOreContainers)
        {
            var isConnected = false;
            foreach(var compContainer in CompContainers)
            {
                if (oreContainer.GetInventory(0).IsConnectedTo(compContainer.GetInventory(0)))
                {
                    isConnected = true;
                    break;
                }
            }

            if (!isConnected)
            {
                OreContainers.Remove(oreContainer);
            }
        }

        foreach(var compContainer in checkCompContainers)
        {
            var isConnected = false;
            foreach(var oreContainer in OreContainers)
            {
                if (oreContainer.GetInventory(0).IsConnectedTo(compContainer.GetInventory(0)))
                {
                    isConnected = true;
                    break;
                }
            }
            if (!isConnected)
            {
                CompContainers.Remove(compContainer);
            }
        }

        if (OreContainers.Count == 0)
        {
            Echo("No Ore container found");
            SetupValid = false;
        }
        if (CompContainers.Count == 0)
        {
            Echo("No Comp container found");
            SetupValid = false;
        }

        if (!SetupValid)
        {
            Runtime.UpdateFrequency = UpdateFrequency.Once;
            return;
        }

        foreach(var container in tempAllContainers)
        {
            var isConnected = false;
            var inventory = container.GetInventory(0);
            foreach(var oreContainer in OreContainers)
            {
                if (oreContainer.GetInventory(0).IsConnectedTo(inventory))
                {
                    isConnected = true;
                    break;
                }
            }
            foreach(var compContainer in CompContainers)
            {
                if (isConnected ||
                    compContainer.GetInventory(0).IsConnectedTo(inventory))
                {
                    isConnected = true;
                    break;
                }
            }

            if (isConnected)
            {
                AllContainers.Add(container);
            }
        }

        SetupValid = true;
        Echo("Setup Complete!");
    }



    public void Main(string argument, UpdateType updateSource)

    {
        
        if (!SetupValid) return;
        Echo("Running...");

        foreach(var container in AllContainers)
        {
            var items = new List<MyInventoryItem>();
            var inventory = container.GetInventory(0);
            inventory.GetItems(items);
            foreach(var item in items)
            {
                if ((item.Type.TypeId.Contains("Ore") || item.Type.TypeId.Contains("Ingot")) &&
                    !container.DisplayNameText.StartsWith("Ore"))
                {
                    foreach(var oreContainer in OreContainers)
                    {
                        if (!oreContainer.GetInventory(0).IsFull)
                        {
                            inventory.TransferItemTo(oreContainer.GetInventory(0), item, item.Amount);
                            break;
                        }
                    }
                    
                }
                else if (item.Type.TypeId.Contains("Component") &&
                        !container.DisplayNameText.StartsWith("Comp"))
                {
                    foreach(var compContainer in CompContainers)
                    {
                        if (!compContainer.GetInventory(0).IsFull)
                        {
                            inventory.TransferItemTo(compContainer.GetInventory(0), item, item.Amount);
                        }
                    }
                }
            }
        }

        foreach(var assmebler in Assemblers)
        {
            var items = new List<MyInventoryItem>();
            var inventory = assmebler.GetInventory(1);
            inventory.GetItems(items);
            foreach(var item in items)
            {
                foreach(var container in CompContainers)
                {
                    var containerInventory = container.GetInventory(0);
                    if (containerInventory.IsFull) continue;
                    inventory.TransferItemTo(containerInventory, item, item.Amount);
                }
            }
        }
    }


    // ------------------------------------------
}