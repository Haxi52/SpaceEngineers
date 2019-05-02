using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
// using VRage.Game.ModAPI.Ingame;


namespace SunTracker
{
    public class Program : Sandbox.ModAPI.Ingame.MyGridProgram
    {
// -------------------------------------------------------------------

        SunTracker tracker1;
        SunTracker tracker2;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;

            tracker1 = new SunTracker(GridTerminalSystem, "Solar Panel Control1", "Solar Rotor 1a", "Solar Rotor 1b", Echo);
            tracker2 = new SunTracker(GridTerminalSystem, "Solar Panel Control2", "Solar Rotor 2a", "Solar Rotor 2b", Echo);


            if (tracker1.Error)
            {
                Runtime.UpdateFrequency = UpdateFrequency.None;
                return;
            }

            Echo("Running");
        }

        public void Main(string argument, UpdateType updateSource)
        {
            tracker1.Update();
            tracker2.Update();
        }

        public class SunTracker 
        {
            private static readonly float rotorSpeed = 0.1f;
            private static readonly int waitForSleep = 20;
            private static readonly int maxOffsetSeek = 3;
            private readonly System.Action<string> echo;
            TrackingState trackingState;

            IMyPowerProducer solarPanel;
            IMyMotorStator rotor1;
            IMyMotorStator rotor2;

            float bestPower = 0;
            float currentPower { get { return solarPanel.MaxOutput; } }
            int offset = 0;

            int sleepWaitCount = 0;
            public bool Error { get; private set; }

            public SunTracker(IMyGridTerminalSystem terminal, string solarPanelName, string rotor1Name, string rotor2Name, System.Action<string> echo)
            {
                Error = false;
                trackingState = TrackingState.Sleep;
                this.echo = echo;

                solarPanel = terminal.GetBlockWithName(solarPanelName) as IMyPowerProducer;
                rotor1 = terminal.GetBlockWithName(rotor1Name) as IMyMotorStator;
                rotor2 = terminal.GetBlockWithName(rotor2Name) as IMyMotorStator;

                if (solarPanel == null)
                {
                    echo("Unable to locate solar panel: " + solarPanelName);
                    Error = true;
                }

                if (rotor1 == null)
                {
                    echo("Unable to locate rotor: " + rotor1Name);
                    Error = true;
                }

                if (!solarPanel.IsFunctional)
                {
                    echo("Solar panel is not functional");
                    Error = true;
                }

                if (!rotor1.IsFunctional)
                {
                    echo("Rotor is not functional");
                    Error = true;
                }

                if (rotor2 != null && !rotor2.IsFunctional)
                {
                    echo("Rotor is not functional");
                    Error = true;
                    return;
                }


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