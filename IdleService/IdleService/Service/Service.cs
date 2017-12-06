﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Topshelf;
using System.Timers;
using NamedPipeWrapper;
using Message;
using System.IO.Pipes;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;

namespace IdleService
{
    class MyService
    {

        enum PacketID
        {
            Hello,
            Goodbye,
            Idle,
            Pause,
            Resume
        }

        #region Json API for XMR-STAK-CPU only
        public class Hashrate
        {
            public List<List<double?>> threads { get; set; }
            public List<double?> total { get; set; }
            public double highest { get; set; }
        }

        public class Results
        {
            public int diff_current { get; set; }
            public int shares_good { get; set; }
            public int shares_total { get; set; }
            public double avg_time { get; set; }
            public int hashes_total { get; set; }
            public List<int> best { get; set; }
            public List<object> error_log { get; set; }
        }

        public class Connection
        {
            public string pool { get; set; }
            public int uptime { get; set; }
            public int ping { get; set; }
            public List<object> error_log { get; set; }
        }

        public class XmrRoot
        {
            public Hashrate hashrate { get; set; }
            public Results results { get; set; }
            public Connection connection { get; set; }
        }
        #endregion

        //TopShelf service controller
        private HostControl host;

        //Pipe that is used to connect to the IdleMon running in the user's desktop session
        internal NamedPipeClient<IdleMessage> client; // = new NamedPipeClient<IdleMessage>(@"Global\MINERPIPE");
        private Timer minerTimer = new Timer(10000);
        private Timer sessionTimer = new Timer(60000);
        private Timer apiCheckTimer = new Timer(10000);
        
        #region TopShelf Start/Stop/Abort
        public bool Start(HostControl hc)
        {
            Utilities.Log("Starting service");
            host = hc;

            if (!Config.configInitialized)
            {
                Utilities.Log("Configuration not loaded; something went wrong!", force: true);
                //host.Stop();
                return false;
            }

            if (!Config.serviceInitialized)
            {
                Utilities.Log("isSys: " + Utilities.IsSystem() + " - " + Environment.UserName);
                SystemEvents.PowerModeChanged += OnPowerChange;
                Initialize();
                client.ServerMessage += OnServerMessage;
                client.Error += OnError;
                client.Disconnected += OnServerDisconnect;
            }

            try
            {
                //Utilities.KillProcess(sessionExeName);
                //Utilities.KillProcess(minerExeName);
            }
            catch (Exception ex)
            {
                Utilities.Log(ex.Message);
            }

            Config.isPipeConnected = false;
            minerTimer.Start();
            sessionTimer.Start();
            apiCheckTimer.Start();
            
            client.Start();
            Config.currentSessionId = ProcessExtensions.GetSession();

            Utilities.CheckForSystem(Config.currentSessionId);

            Utilities.Log("IdleService running.");
            return true;
        }

        public void Stop()
        {

            Utilities.Log("Stopping IdleService..");
            minerTimer.Stop();
            sessionTimer.Stop();
            apiCheckTimer.Stop();
            client.Stop();

            Config.isCurrentlyMining = false;

            Utilities.AllowSleep();
            Utilities.KillMiners();
            Utilities.KillProcess(Config.idleMonExecutable);
            Utilities.Log("Successfully stopped IdleService.");
        }

        public void Abort()
        {
            host.Stop();
        }

        private void Initialize()
        {

            Utilities.Log("Initializing IdleService.. CPU Cores: " + Environment.ProcessorCount);
            
            if (Utilities.DoesBatteryExist())
            {
                Config.doesBatteryExist = true;
                Utilities.Log("Battery found.");
            }

            minerTimer.Elapsed += OnMinerTimerEvent;
            sessionTimer.Elapsed += OnSessionTimer;

            minerTimer.AutoReset = true;

            Config.serviceInitialized = true;

            client = new NamedPipeClient<IdleMessage>(@"Global\MINERPIPE");

        }

        #endregion

        #region NamedPipe Events
        private void OnServerDisconnect(NamedPipeConnection<IdleMessage, IdleMessage> connection)
        {
            Utilities.Log("IdleService Pipe disconnected");
            Config.isPipeConnected = false;
        }

        private void OnError(Exception exception)
        {
            Utilities.Log("IdleService Pipe Err: " + exception.Message);
            Config.isPipeConnected = false;

            client.Stop();
            client.Start();
            
        }

        private void OnServerMessage(NamedPipeConnection<IdleMessage, IdleMessage> connection, IdleMessage message)
        {
            Config.sessionLaunchAttempts = 0;
            Config.isPipeConnected = true;
            switch (message.request)
            {
                case ((int)PacketID.Idle):
                    Utilities.Log("Idle received from " + message.Id + ": " + message.isIdle);

                    if (Config.isUserLoggedIn)
                        Config.isUserIdle = message.isIdle;

                    /*
                     * Potentially allow launching miners in the user's desktop session
                     * instead of launching in SYSTEM context with a pipe message
                    connection.PushMessage(new IdleMessage
                    {
                        Id = System.Diagnostics.Process.GetCurrentProcess().Id,
                        isIdle = false,
                        request = (int)PacketID.idle
                    });
                    */

                    break;

                case ((int)PacketID.Pause):
                    Config.isMiningPaused = true;
                    Utilities.KillMiners();
                    break;

                case ((int)PacketID.Resume):
                    Config.isMiningPaused = false;
                    //resume mining
                    break;                    

                case ((int)PacketID.Hello):
                    Utilities.Log("idleMon user " + message.data + " connected " + message.Id);
                    Config.isUserIdle = message.isIdle;
                    break;

                default:
                    //Utilities.Log("IdleService Idle default: " + message.request);
                    break;
            }
        }
#endregion
        
        public void SessionChanged(SessionChangedArguments args)
        {
            //todo Put this somewhere better
            Utilities.KillMiners();

            switch (args.ReasonCode)
            {
                case Topshelf.SessionChangeReasonCode.SessionLock:
                    Utilities.Log(string.Format("Session: {0} - Reason: {1} - Lock", args.SessionId, args.ReasonCode));
                    Config.isUserLoggedIn = false;
                    Config.currentSessionId = args.SessionId;
                    Config.isUserIdle = true;
                    break;

                case Topshelf.SessionChangeReasonCode.SessionLogoff:
                    Config.isUserLoggedIn = false;
                    Utilities.Log(string.Format("Session: {0} - Reason: {1} - Logoff", args.SessionId, args.ReasonCode));
                    Config.currentSessionId = 0;
                    Config.isUserIdle = true;
                    break;

                case Topshelf.SessionChangeReasonCode.SessionUnlock:
                    Config.isUserLoggedIn = true;
                    Utilities.Log(string.Format("Session: {0} - Reason: {1} - Unlock", args.SessionId, args.ReasonCode));
                    Config.currentSessionId = args.SessionId;
                    Config.isUserIdle = false;
                    break;

                case Topshelf.SessionChangeReasonCode.SessionLogon:
                    Config.isUserLoggedIn = true;
                    Utilities.Log(string.Format("Session: {0} - Reason: {1} - Login", args.SessionId, args.ReasonCode));
                    Config.currentSessionId = args.SessionId;
                    Config.isUserIdle = false;
                    break;

                case Topshelf.SessionChangeReasonCode.RemoteDisconnect:
                    Config.isUserLoggedIn = false;
                    Utilities.Log(string.Format("Session: {0} - Reason: {1} - RemoteDisconnect", args.SessionId, args.ReasonCode));
                    Config.currentSessionId = ProcessExtensions.GetSession();
                    if (Config.currentSessionId > 0)
                        Config.isUserLoggedIn = true;
                    Config.isUserIdle = true;
                    break;

                case Topshelf.SessionChangeReasonCode.RemoteConnect:
                    Config.isUserLoggedIn = true;
                    Utilities.Log(string.Format("Session: {0} - Reason: {1} - RemoteConnect", args.SessionId, args.ReasonCode));
                    Config.currentSessionId = ProcessExtensions.GetSession();
                    Config.isUserIdle = false;
                    break;

                default:
                    Utilities.Log(string.Format("Session: {0} - Other - Reason: {1}", args.SessionId, args.ReasonCode));
                    break;
            }

        }

        private void OnPowerChange(object sender, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Resume:
                    Utilities.Log("Resuming");
                    Start(host);
                    break;
                case PowerModes.Suspend:
                    Utilities.Log("Suspending");
                    Stop();
                    break;
                case PowerModes.StatusChange:
                    Utilities.Log("Power changed"); // ie. weak battery
                    break;

                default:
                    Utilities.Log("OnPowerChange: " + e.ToString());
                    break;
            }
        }
        
        #region Old API Json reading section (not used)
        /*
        private async Task<String> getTestObjects(string url)
        {
            var httpClient = new HttpClient();
            httpClient.Timeout = System.TimeSpan.FromMilliseconds(2000);
            var response = await httpClient.GetAsync(url);
            var result = await response.Content.ReadAsStringAsync();

            return result;
        }

        
        private void OnApiTimer(object sender, ElapsedEventArgs e)
        {

            //this timer is disabled, but just in case let's return out of it asap as it's not setup for xmrig
            return;

            try
            {
                if (Config.isCurrentlyMining != true)
                    return;

                XmrRoot test = JsonConvert.DeserializeObject<XmrRoot>(getTestObjects("http://127.0.0.1:16000/api.json").Result);

                //Utilities.Log("Pool: " + test.connection.pool);
                //Utilities.Log("Average Hashrate: " + test.hashrate.total.Average());
                //Utilities.Log("Highest Hashrate: " + test.hashrate.highest);
                //Utilities.Log("Shares Acc: " + test.results.shares_good);
                //Utilities.Log("Uptime: " + test.connection.uptime);
                //Utilities.Log("Ping: " + test.connection.ping);

                if (test.connection.uptime == 0)
                    failUptime++;
                else
                    failUptime = 0;

                
                //if (test.hashrate.total.Average() <= 5)
                //    failHashrate++;
                //else
                //    failHashrate = 0;
                

                if (failUptime >= 5 || failHashrate >= 5)
                {
                    failUptime = 0;
                    failHashrate = 0;
                    Utilities.KillProcess();

                }

                //Utilities.Log(failHashrate + " - " + failUptime);

            } catch (Exception ex)
            {
                Utilities.Log("api: " + ex.Message);
            }
        }
        */
#endregion

        #region Timers/Events
        private void OnSessionTimer(object sender, ElapsedEventArgs e)
        {
            Config.currentSessionId = ProcessExtensions.GetSession();

            Utilities.Log("OnSessionTimer: SessionID " + Config.currentSessionId);

            Utilities.CheckForSystem(Config.currentSessionId);

            //Utilities.Log(string.Format("Session: {0} - isLoggedIn: {1} - connected: {2} - sessionFail: {3} - isIdle: {4}", currentSession, isLoggedIn, connected, sessionFail, isIdle));

            if (Config.sessionLaunchAttempts > 4)
            {
                Utilities.Log("Unable to start IdleMon in user session; stopping service.", force: true);
                host.Stop();
                return;
            }

            if (Config.isUserLoggedIn && !Config.isPipeConnected)
            {
                Config.sessionLaunchAttempts++;
                Utilities.KillProcess(Config.idleMonExecutable);

                string args = Config.settings.stealthMode ? "-stealth" : "";
                args += Config.settings.enableLogging ? "-log" : "";

                ProcessExtensions.StartProcessAsCurrentUser(Config.idleMonExecutable, args, null, false);
                Utilities.Log("Attempting to start IdleMon in " + Config.currentSessionId);
                return;
            }
            else if (!Config.isUserLoggedIn && Config.isPipeConnected)
            {
                Config.sessionLaunchAttempts = 0;
                Utilities.KillProcess(Config.idleMonExecutable);
            }
            else if (!Config.isUserLoggedIn)
            {
                Config.sessionLaunchAttempts = 0;
                
                if (Config.currentSessionId > 0)
                    Config.isUserLoggedIn = true;
            }
        }

        private void OnMinerTimerEvent(object sender, ElapsedEventArgs e)
        {
            
            lock (Config.timeLock)
            {
                
                if (Config.skipTimerCycles > 0)
                {
                    Config.skipTimerCycles--;
                    return;
                }

                if (Config.isMiningPaused)
                    return;

                //If not idle, and currently mining
                if ((!Config.isUserIdle && Config.isCurrentlyMining))
                {   
                    //If our CPU threshold is over 0, and CPU usage is over that, then stop mining and skip the next 6 timer cycles
                    if (Config.settings.cpuUsageThresholdWhileNotIdle > 0 && (Utilities.GetCpuUsage() > Config.settings.cpuUsageThresholdWhileNotIdle))
                    {
                        Utilities.KillMiners();
                        Config.skipTimerCycles = 6;
                        return;
                    }
                }

                if (Config.doesBatteryExist && !Utilities.IsBatteryFull())
                {
                    if (Config.isCurrentlyMining)
                    {
                        Utilities.Log("Battery level is not full; stop mining..");
                        Utilities.KillMiners();
                    }
                    // regardless if we're mining, we can exit this method as we don't want to start mining now
                    return;
                }

                //check if resumePausedMiningAfterMinutes has passed, eventually..

                bool cpuMinersRunning = Utilities.AreMinersRunning(Config.settings.cpuMiners);
                bool gpuMinersRunning = Utilities.AreMinersRunning(Config.settings.gpuMiners);

                if (!cpuMinersRunning)
                    Utilities.LaunchMiners(Config.settings.cpuMiners);

                if (!cpuMinersRunning)
                    Utilities.LaunchMiners(Config.settings.gpuMiners);
                
                //Check cpu/gpu miners running, if not all running, start the ones that aren't running

                //Prevent sleep

                //check cpu/gpu temps

            }
        }
#endregion
        
        public void StartMiner(bool lowCpu)
        {

            lock (Config.startLock)
            {
                int pid = 0;

                //Utilities.Log("StartM..");

                if (!File.Exists(""))
                {
                    Utilities.Log("" + " doesn't exist");
                    Abort();
                }
                if (Utilities.IsProcessRunning(""))
                {
                    Utilities.Log("Already running, but startm?");
                    return;
                }

                try
                {
                    if (lowCpu)
                    {

                        //todo: Launch all miners in LOW CPU MODE from list
                        //pid = LaunchProcess(minerExe, lowCpuConfig);
                        //Utilities.Log("Started lowcpu mining: " + pid);
                        Config.isIdleMining = false;
                    }
                    else
                    {
                        //var count = Environment.ProcessorCount;
                        //todo: Launch all miners from IDLE CPU MODE (High speed mode) from list
                        
                        //Utilities.Log("Started idlecpu mining: " + pid);
                        Config.isIdleMining = true;
                    }

                    //Sets whether the miner is running based on Process ID from the Launch method
                    Config.isCurrentlyMining = (pid > 0);

                    Utilities.Log("Config.isIdleMining: " + Config.isIdleMining + " - running: " + Config.isCurrentlyMining);
                }
                catch (Exception ex)
                {
                    Utilities.Log("cpu ex:" + ex.Message + Environment.NewLine + ex.StackTrace);
                    
                    if (ex.Message.Contains("platform"))
                    {
                        Abort();
                    }
                }
            }
        }
        
        public void Uninstall()
        {
            //Run an external batch file to clean up the miner and remove all traces of it
            //todo: this, and move to Utilities
        }
                        
    }
}
