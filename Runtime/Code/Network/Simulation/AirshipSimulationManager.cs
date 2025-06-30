using System;
using System.Collections.Generic;
using System.Linq;
using Code.Player;
using Mirror;
using Tayx.Graphy.Resim;
using UnityEngine;
using UnityEngine.Serialization;

namespace Code.Network.Simulation
{
    /**
     * Callback used to check the world state at the time requested. You should consider the physics world
     * to be read only while this function executes. Changes to the physics state will be overwritten after
     * your function returns. Use the RollbackComplete callback to modify the physics world based on the
     * results of your check.
     */
    public delegate void CheckWorld();

    /**
     * Callback used to modify physics in the next server tick. The physics world is set to the most recent
     * server tick, and can be modified freely. These modifications will be reconciled to the clients as part
     * of the next server tick.
     *
     * Use this callback to do things like add impulses to hit characters, move them, or anything else that
     * changes physics results.
     */
    public delegate void RollbackComplete();

    /**
     * Requests a simulation based on the provided tick. Requesting a simulation will roll back the physics
     * world to the snapshot just before or at the base tick provided. Calling the returned tick function
     * will advance the simulation and re-simulate the calls to OnPerformTick, Physics.Simulate(), and OnCaptureSnapshot
     *
     * When this call completes, the world will be at the last completed tick.
     */
    public delegate void PerformResimulate(uint baseTick);

    /**
     * Function that will be run when the simulation manager is ready to perform a resimulation. Remember that
     * the simulation base time provided to the resimulate function is the **local time** you wish to resimulate from.
     * The local time should be one provided by the SimulationManager during a tick.
     * Do not pass NetworkTime.time or a similar server derived time to this resimulate function.
     *
     * This function should not be used on the server.
     */
    public delegate void PerformResimulationCallback(PerformResimulate simulateFunction);

    struct LagCompensationRequest
    {
        public CheckWorld check;
        public RollbackComplete complete;
    }

    struct ResimulationRequest
    {
        public PerformResimulationCallback callback;
    }

    /**
     * The simulation manager is responsible for calling Physics.Simulate and providing generic hooks for other systems to use.
     * Server authoritative networking uses the simulation manager to perform resimulations of its client predictions.
     */
    [LuauAPI]
    public class AirshipSimulationManager : Singleton<AirshipSimulationManager>
    {
        /**
         * This function notifies all watching components that a re-simulation
         * is about to occur. The boolean parameter will be true if a re-simulation
         * is about to occur, and will be false if the re-simulation has finished.
         *
         * Most components watching this will want to set their rigidbodies to
         * kinematic if they do not wish to take part in the re-simulation. Physics
         * will be ticked during re-simulation.
         */
        public event Action<bool> OnSetPaused;

        /**
         * This action notifies all watching components that they need to set their
         * state to be based on the snapshot captured just before or on the provided
         * time. Components should expect a PerformTick() call sometime after this
         * function completes.
         */
        public event Action<object> OnSetSnapshot;

        /**
         * This action notifies listeners that we are performing a lag compensation check.
         * This action is only ever invoked on the server. Components listening to this
         * action should set their state to be what the client would have seen at the provided
         * tick time. Keep in mind, this means that any components the client would have been
         * observing (ie. other player characters) should be rolled back an additional amount to account for the client
         * interpolation. You can convert a time to an exact tick time using
         * GetLastSimulationTime() to find the correct tick time for any given time in the
         * last 1 second.
         *
         * After a lag compensation check is completed, OnSetSnapshot will be called to correct
         * the physics world to it's current state.
         *
         * clientId - The connectionId of the client we are simulating the view of
         * tick - The tick that triggered this compensation check
         * time - unscaled time for the tick
         * rtt - The estimated time it takes for a message to reach the client and then be returned to the server (aka. ping) (rtt / 2 = latency)
         */
        public event Action<int, uint, double, double> OnLagCompensationCheck;

        /// <summary>
        /// This action tells all watching components that they need to perform a tick.
        /// A Physics.Simulate() call will be made after PerformTick completes.
        /// params:
        /// -tick - the tick number
        /// -time - unscaled time of the tick
        /// -replay - if this is a replay of a tick
        /// </summary>
        public event Action<object, object, object> OnTick;

        /**
         * Informs all watching components that the simulation tick has been performed
         * and that a new snapshot of the resulting Physics.Simulate() should be captured.
         * This snapshot should be the state for the provided tick number in history.
         */
        public event Action<uint, double, bool> OnCaptureSnapshot;

        /**
         * Fired when a tick leaves local history and will never be referenced again. You can use this
         * event to clean up any data that is no longer required.
         */
        public event Action<object> OnHistoryLifetimeReached;
        
        /**
        * Fired when lag compensated checks should occur. ID of check is passed as the event parameter.
        */
        public event Action<object> OnLagCompensationRequestCheck;
        /**
         * Fired when lag compensated check is over and physics can be modified. ID of check is passed as the event parameter.
         */
        public event Action<object> OnLagCompensationRequestComplete;

        [NonSerialized] public bool replaying = false;
        [NonSerialized] public uint tick;
        [NonSerialized] public double time;
        
        private bool isActive = false;
        private List<uint> previousTicks = new List<uint>();
        private Dictionary<uint, double> tickTimes = new();
        private Dictionary<NetworkConnectionToClient, List<LagCompensationRequest>> lagCompensationRequests = new();
        private Queue<ResimulationRequest> resimulationRequests = new();

        public void ActivateSimulationManager()
        {
            if (isActive) return;
            Physics.simulationMode = SimulationMode.Script;
            this.isActive = true;
        }
        
        public void FixedUpdate()
        {
            if (!isActive) return;
            
            // --- Notes about tick calculation ---
            
            // Clients use their own timelines for physics. Do not compare ticks generated on a client with a tick generated
            // on the server. The Server should estimate when a client created a command using it's own timeline and ping calculations
            // and a client should convert observed server authoritative state received to its own timeline by interpolating with NetworkTime.time
            // and capturing snapshots of the interpolated state on its own timeline.
            
            // The calculation below creates a tick number that always increases by one (meaning it is affected by timescale). Unscaled fixed
            // time is also passed through and is used for observing characters. We use unscaled fixed time so that we can always
            // display a smooth observed player using NetworkTime.time which uses unscaled time.
            
            // ---
            tick = (uint)Mathf.RoundToInt(Time.fixedTime / Time.fixedDeltaTime);
            time = Time.fixedUnscaledTimeAsDouble; // TODO: pass this time to the callback functions so they can always use the same time values during replays. Will need to be tracked
            
            // Update debug overlay
            var tickGenerationTime = Time.fixedDeltaTime / Time.timeScale; // how long it takes to generate a single tick in real time.
            G_ResimMonitor.FrameObserverBuffer =  NetworkClient.bufferTime + tickGenerationTime;
            
            if (Physics.simulationMode != SimulationMode.Script) {
                // reset the simulation mode if it changed for some reason. This seems to happen on the server when you change prefabs
                // while in play mode with mppm.
                Physics.simulationMode = SimulationMode.Script;
            }
            
            // Before running any commands, we perform any resimulation requests that were made during
            // the last tick. This ensures that resimulations don't affect command processing and
            // that all commands run on the most up to date predictions.
            var resimBackTo = tick;
            while (this.resimulationRequests.TryDequeue(out ResimulationRequest request))
            {
                try
                {
                    request.callback((requestedTick) =>
                    {
                        if (resimBackTo > requestedTick) resimBackTo = requestedTick;
                    });
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }

            // Only resimulate once. Go back to the farthest back time that was requested.
            if (resimBackTo != tick) this.PerformResimulation(resimBackTo);

            // Perform the standard tick behavior
            OnTick?.Invoke(tick, time, false);
            // Debug.Log($"Simulate call. Main tick {tick}");
            Physics.Simulate(Time.fixedDeltaTime);
            OnCaptureSnapshot?.Invoke(tick, time, false);

            // Process any lag compensation requests now that we have completed the ticking and snapshot creation
            // Note: This process is placed after snapshot processing so that changes made to physics (like an impulse)
            // are processed on the _next_ tick. This is safe because the server never resimulates.
            var processedLagCompensation = false;
            foreach (var entry in this.lagCompensationRequests)
            {
                try
                {
                    processedLagCompensation = true;
                    // Debug.LogWarning("Server lag compensation rolling back for client " + entry.Key.connectionId);
                    OnLagCompensationCheck?.Invoke(entry.Key.connectionId, tick, time, entry.Key.rtt);
                    foreach (var request in entry.Value)
                    {
                        request.check();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }

            // If we processed lag compensation, we have some additional work to do
            if (processedLagCompensation)
            {
                // Debug.LogWarning("Server completed " + this.lagCompensationRequests.Count + " lag compensation requests. Resetting to current tick (" + time + ") and finalizing.");
                // Reset back to the server view of the world at the current time.
                OnSetSnapshot?.Invoke(tick);
                // Invoke all of the callbacks for modifying physics that should be applied in the next tick.
                foreach (var entry in this.lagCompensationRequests)
                {
                    foreach (var request in entry.Value)
                    {
                        try
                        {
                            request.complete();
                        }
                        catch (Exception e)
                        {
                            Debug.LogError(e);
                        }
                    }
                }
                this.lagCompensationRequests.Clear();
            }

            // Add our completed tick time into our history
            this.previousTicks.Add(tick);
            this.tickTimes.Add(tick, time);
            // Keep the tick history around only for 1 second. This limits our lag compensation amount.
            var ticksPerSecond = 1 / Time.fixedDeltaTime;
            while (this.previousTicks.Count > 0 && tick - this.previousTicks[0] > ticksPerSecond)
            {
                OnHistoryLifetimeReached?.Invoke(this.previousTicks[0]);
                this.tickTimes.Remove(this.previousTicks[0]);
                this.previousTicks.RemoveAt(0);
            }
        }
        
        /**
         * Submits callbacks to be run later that will be able to view the physics world as the client
         * would have seen it at the current tick. This allows you to confirm if a clients input would
         * have hit a target from their point of view.
         *
         * This uses the clients estimated round trip time to determine what tick the client was likely
         * seeing and rolls back Physics to that tick. Once physics is rolled back, the callback function
         * is executed.
         */
        public void ScheduleLagCompensation(NetworkConnectionToClient client, CheckWorld checkCallback,
            RollbackComplete completeCallback)
        {
            List<LagCompensationRequest> list;
            if (!this.lagCompensationRequests.TryGetValue(client, out list))
            {
                list = new();
                this.lagCompensationRequests.Add(client, list);
            }

            list.Add(new LagCompensationRequest()
            {
                check = checkCallback,
                complete = completeCallback,
            });
        }

        /**
         * Schedules lag compensation for the provided ID. Used by TS to call ScheduleLagCompensation for a player in the Player.ts file.
         */
        public string RequestLagCompensationCheck(int connectionId)
        {
            if (NetworkServer.connections.TryGetValue(connectionId, out var connection))
            {
                string uniqueId = Guid.NewGuid().ToString();
                this.ScheduleLagCompensation(connection, () => {
                    this.OnLagCompensationRequestCheck?.Invoke(uniqueId);
                }, () =>
                {
                    this.OnLagCompensationRequestComplete?.Invoke(uniqueId);
                });
                return uniqueId;
            }

            return "";
        }

        /**
         * Schedules a resimulation to occur on the next tick. This allows correcting predicted history on a non authoritative client.
         * The callback provided will be called when the resimulation should occur. The callback will be passed a resimulate
         * function to trigger a resimulation of all ticks from the provided base time back to the present time.
         */
        public void ScheduleResimulation(PerformResimulationCallback callback)
        {
            this.resimulationRequests.Enqueue(new ResimulationRequest() { callback = callback });
        }

        /**
         * Allows typescript to request a resimulation from the provided time.
         */
        public void RequestResimulation(uint tick)
        {
            this.ScheduleResimulation((resim => resim(tick)));
        }

        /// <summary>
        /// Requests a simulation based on the provided time. Requesting a simulation will roll back the physics
        /// world to the snapshot just before or at the base time provided. Calling the returned tick function
        /// will advance the simulation and re-simulate the calls to <see cref="OnTick"/>, Physics.Simulate,
        /// and <see cref="OnCaptureSnapshot"/>
        /// 
        /// This function is used internally to implement the scheduled resimulations.
        /// </summary>
        private void PerformResimulation(uint baseTick)
        {
            // Debug.Log($"T:{Time.unscaledTimeAsDouble} Resimulating from {baseTick} to {this.previousTicks[^1]}");
            G_ResimMonitor.FrameResimValue = 100;
            var prevTick = tick;
            var prevTime = time;
            
            if (replaying)
            {
                Debug.LogWarning("Re-simulation already active. This is unrecoverable.");
                throw new ApplicationException(
                    "Re-simulation requested while a re-simulation is already active. Report this.");
            }

            if (previousTicks.Count == 0) {
                Debug.LogWarning($"Re-simulation request had base tick {baseTick}, but we have no tick history. Request will be ignored.");
                return;
            }

            // If the base time further in the past that our history goes, we reset to the oldest history we have (0) instead.
            uint targetTick = baseTick;
            if (baseTick < previousTicks[0]) {
                targetTick = previousTicks[0];
            }

            this.replaying = true;
            try
            {
                OnSetPaused?.Invoke(true);
                OnSetSnapshot?.Invoke(targetTick);
                Physics.SyncTransforms();
                // Advance the tick so that we are re-processing the next tick after the base time provided.
                targetTick++;

                while (targetTick <= this.previousTicks[^1]) {
                    tickTimes.TryGetValue(targetTick, out double targetTime);
                    tick = targetTick;
                    time = targetTime;
                    OnTick?.Invoke(targetTick, targetTime, true);
                    // Debug.Log("Simulate call. Replay Tick: " + tick);
                    Physics.Simulate(Time.fixedDeltaTime);
                    OnCaptureSnapshot?.Invoke(targetTick, targetTime, true);
                    targetTick++;
                }

                OnSetPaused?.Invoke(false);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                this.replaying = false;
                tick = prevTick;
                time = prevTime;
                Debug.Log($"Completed resimulation from {baseTick} to {this.previousTicks[^1]}");
            }
        }
    }
}