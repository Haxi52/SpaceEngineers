using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
// using VRage.Game.ModAPI.Ingame;


namespace SunTracker2
{
    public class Program : Sandbox.ModAPI.Ingame.MyGridProgram
    {
// -------------------------------------------------------------------

        private static string SolarTrackingSectionName = "solartracking";
        private static string SolarBatterySectionName = "solarbattery";
        private static string Rotor1KeyName = "rotor1";
        private static string Rotor2KeyName = "rotor2";

        private List<SunTracker> trackers = new List<SunTracker>();

        private List<IMyBatteryBlock> Batteries = new List<IMyBatteryBlock>();

        public Program()
        {
            var solarPanels = new List<IMyPowerProducer>();
            GridTerminalSystem.GetBlocksOfType<IMyPowerProducer>(solarPanels,
                    block => MyIni.HasSection(block.CustomData, SolarTrackingSectionName));
            
            var ini = new MyIni();
            MyIniParseResult parseResult;
            foreach(var solarPanel in solarPanels)
            {
                if (!ini.TryParse(solarPanel.CustomData, out parseResult))
                {
                    Echo("Unable to parse config for " + solarPanel.DisplayName);
                    continue;
                }
                var rotor1Name = ini.Get(SolarTrackingSectionName, Rotor1KeyName).ToString();
                var rotor1 = GridTerminalSystem.GetBlockWithName(rotor1Name) as IMyMotorStator;

                IMyMotorStator rotor2 = null;
                if (ini.ContainsKey(SolarTrackingSectionName, Rotor2KeyName))
                {
                    var rotor2Name = ini.Get(SolarTrackingSectionName, Rotor2KeyName).ToString();
                    rotor2 = GridTerminalSystem.GetBlockWithName(rotor2Name) as IMyMotorStator;
                }

                if (rotor1 == null)
                {
                    Echo("Unable to locate rotor: " + rotor1Name);
                    continue;
                }

                if (!solarPanel.IsFunctional)
                {
                    Echo("Solar panel is not functional");
                    continue;
                }

                if (!rotor1.IsFunctional)
                {
                    Echo("Rotor is not functional");
                    continue;
                }

                if (rotor2 != null && !rotor2.IsFunctional)
                {
                    Echo("Rotor is not functional");
                    continue;
                }

                trackers.Add(new SunTracker(solarPanel, rotor1, rotor2, Echo));
            }

            if (trackers.Count == 0)
            {
                Runtime.UpdateFrequency = UpdateFrequency.None;
                return;
            }

            GridTerminalSystem.GetBlocksOfType(Batteries, bat => bat.IsFunctional && MyIni.HasSection(bat.CustomData, SolarBatterySectionName));

            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            Echo("Running");
        }

        public void Main(string argument, UpdateType updateSource)
        {
            bool hasPower = false;
            foreach(var tracker in trackers)
            {
                tracker.Update();
                hasPower = hasPower || tracker.HasPower;
            }

            if (Batteries.Count > 0)
            {
                var chargeMode = hasPower ? ChargeMode.Recharge : ChargeMode.Discharge;

                foreach(var bat in Batteries)
                {
                    bat.ChargeMode = chargeMode;
                }
            }
        }

        public class SunTracker 
        {
            private static readonly float rotorSpeed = 0.1f;
            private static readonly int waitForSleep = 20;
            private static readonly int maxOffsetSeek = 3;
            private readonly System.Action<string> echo;

            public bool HasPower { get { return solarPanel.MaxOutput > 0; } }

            TrackingState trackingState;

            IMyPowerProducer solarPanel;
            IMyMotorStator rotor1;
            IMyMotorStator rotor2;

            float bestPower = 0;
            float currentPower { get { return solarPanel.MaxOutput; } }
            int offset = 0;

            int sleepWaitCount = 0;

            public SunTracker(IMyPowerProducer sp, IMyMotorStator r1, IMyMotorStator r2, System.Action<string> echo)
            {
                trackingState = TrackingState.Sleep;
                this.echo = echo;

                solarPanel = sp;
                rotor1 = r1;
                rotor2 = r2;

                bestPower = currentPower;
                rotor1.TargetVelocityRPM =0;
                if (rotor2 != null)
                {
                    rotor2.TargetVelocityRPM = 0;
                }
                sleepWaitCount = waitForSleep - 1;
            }

            public void Update()
            {
                // no sun, nothing to do
                if (currentPower == 0 && bestPower != 0) 
                {
                    bestPower = 0;
                    trackingState = TrackingState.Sleep;
                    sleepWaitCount = 0;

                    rotor1.TargetVelocityRPM = 0;

                    if (rotor2 != null)
                    {
                        rotor2.TargetVelocityRPM = 0;
                    }
                    echo("No Power, turning off for now");
                    return;
                }

                switch(trackingState)
                {
                    case TrackingState.Sleep:
                        if (currentPower == 0) return;

                        echo("Sleeping (" + sleepWaitCount.ToString() + ")");
                        // wait a min amount of time before trying to seek the sun again
                        if ((++sleepWaitCount) < waitForSleep) return;

                        // otherwise lets find a better position;
                        trackingState = TrackingState.SeekUp1;
                        rotor1.TargetVelocityRPM = rotorSpeed;
                        bestPower = currentPower;
                        offset = 0;

                        break;
                    case TrackingState.SeekUp1:
                        echo("Tracking Rotor1 up " + offset.ToString());
                        // we found a better position, keep going
                        if (bestPower < currentPower)
                        {
                            bestPower = currentPower;
                            offset = 0;
                            return;
                        }
                        else if (offset < maxOffsetSeek)
                        {
                            offset++;
                            return;
                        }

                        // its going down in that direction, lets try the other way.
                        trackingState = TrackingState.SeekDown1;
                        rotor1.TargetVelocityRPM = -rotorSpeed;
                        bestPower = currentPower;

                        break;
                    case TrackingState.SeekDown1:
                        echo("Tracking Rotor1 down " + offset.ToString());

                        // First return to the best offset vound in seek up 1
                        if (offset > 0)
                        {
                            offset--;
                            return;
                        }

                        // we found a better position, keep going
                        if (bestPower < currentPower)
                        {
                            bestPower = currentPower;
                            offset = 0;
                            return;
                        }
                        // overshoot the offset to double check
                        else if (offset > -maxOffsetSeek)
                        {
                            offset--;
                            return;
                        }

                        rotor1.TargetVelocityRPM = rotorSpeed;
                        trackingState = TrackingState.Reset1;

                        break;
                    case TrackingState.Reset1:
                        echo("Resetting Rotor1 to offset " + offset.ToString()); 

                        if (offset < 0)
                        {
                            offset++;
                            return;
                        }

                        // ok done, stop the rotor
                        rotor1.TargetVelocityRPM = 0;

                        // do we check the second rotor?
                        if (rotor2 != null)
                        {
                            trackingState = TrackingState.SeekUp2;
                            rotor2.TargetVelocityRPM = rotorSpeed;
                            bestPower = currentPower;
                            offset = 0;
                        }
                        else 
                        {
                            trackingState = TrackingState.Sleep;
                            sleepWaitCount = 0;
                        }
                        break;
                    case TrackingState.SeekUp2:
                        echo("Tracking Rotor2 up " + offset.ToString()); 
                          // we found a better position, keep going
                        if (bestPower < currentPower)
                        {
                            bestPower = currentPower;
                            offset = 0;
                            return;
                        }
                        else if (offset < maxOffsetSeek)
                        {
                            offset++;
                            return;
                        }

                        // its going down in that direction, lets try the other way.
                        trackingState = TrackingState.SeekDown2;
                        rotor2.TargetVelocityRPM = -rotorSpeed;
                        bestPower = currentPower;
                        
                        break;
                    case TrackingState.SeekDown2:
                        echo("Tracking Rotor2 down " + offset.ToString());
                        if (offset > 0)
                        {
                            offset--;
                            return;
                        }

                        // we found a better position, keep going
                        if (bestPower < currentPower)
                        {
                            bestPower = currentPower;
                            offset = 0;
                            return;
                        }
                        else if (offset > -maxOffsetSeek)
                        {
                            offset--;
                            return;
                        }

                        rotor2.TargetVelocityRPM = rotorSpeed;
                        trackingState = TrackingState.Reset2;

                        break;
                    case TrackingState.Reset2:
                        echo("Resetting Rotor2 to offset " + offset.ToString()); 
                        if (offset < 0)
                        {
                            offset++;
                            return;
                        }

                        // this should be the best position;
                        rotor2.TargetVelocityRPM = 0;
                        trackingState = TrackingState.Sleep;
                        sleepWaitCount = 0;
                        break;
                    default:
                        return;
                }
            }


            public enum TrackingState
            {
                Sleep =0,
                SeekUp1,
                SeekDown1,
                Reset1,
                SeekUp2,
                SeekDown2,
                Reset2,
            }
        }

// -------------------------------------------------------------------
    }
}