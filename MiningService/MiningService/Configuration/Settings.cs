﻿using Newtonsoft.Json;
using System.Collections.Generic;

namespace MiningService
{
    public class Settings
    {
        [JsonProperty]
        public List<MinerList> cpuMiners = new List<MinerList>();

        [JsonProperty]
        public List<MinerList> gpuMiners = new List<MinerList>();

        //[JsonProperty]
        //public int resumePausedMiningAfterMinutes { get; set; }
        [JsonProperty]
        public List<string> ignoredFullscreenApps = new List<string>();

        [JsonProperty]
        public bool checkIfFullscreenAppStillRunning { get; set; }

        [JsonProperty]
        public int cpuUsageThresholdWhileNotIdle { get; set; }

        //Settings loaded from config file
        [JsonProperty]
        public bool enableDebug { get; set; }

        [JsonProperty]
        public bool enableLogging { get; set; }

        [JsonProperty]
        public int maxCpuTemp { get; set; }

        [JsonProperty]
        public int maxGpuTemp { get; set; }

        [JsonProperty]
        public bool mineIfBatteryNotFull { get; set; }

        [JsonProperty]
        public bool mineWithCpu { get; set; }

        [JsonProperty]
        public bool mineWithGpu { get; set; }

        [JsonProperty]
        public int minutesUntilIdle { get; set; }

        [JsonProperty]
        public bool monitorCpuTemp { get; set; }

        [JsonProperty]
        public bool monitorFullscreen { get; set; }

        [JsonProperty]
        public bool monitorGpuTemp { get; set; }

        [JsonProperty]
        public bool preventSleep { get; set; }

        [JsonProperty]
        public int resumeMiningTempInPercent { get; set; }

        [JsonProperty]
        public bool resumePausedMiningOnLockOrLogoff { get; set; }

        [JsonProperty]
        public bool runInUserSession { get; set; }

        [JsonProperty]
        public bool showDesktopNotifications { get; set; }

        [JsonProperty]
        public string urlToCheckForNetwork { get; set; }

        [JsonProperty]
        public bool verifyNetworkConnectivity { get; set; }

        [JsonProperty]
        public string afterBurnerExePath { get; set; }

        [JsonProperty]
        public bool autoSwitchMsiAfterburnerProfile { get; set; }

        [JsonProperty]
        public int afterBurnerIdleProfile { get; set; }

        [JsonProperty]
        public int afterBurnerActiveProfile { get; set; }

        public void SetupDefaultConfig()
        {
            enableDebug = false;
            enableLogging = true;
            monitorFullscreen = true;
            //stealthMode = false;
            preventSleep = false;
            monitorCpuTemp = false;
            monitorGpuTemp = false;
            maxCpuTemp = 60;
            maxGpuTemp = 75;
            resumeMiningTempInPercent = 5;
            mineWithCpu = true;
            mineWithGpu = true;
            cpuUsageThresholdWhileNotIdle = 90;
            mineIfBatteryNotFull = false;
            verifyNetworkConnectivity = false;
            urlToCheckForNetwork = "http://google.com";
            autoSwitchMsiAfterburnerProfile = true;
            minutesUntilIdle = 10;
            cpuMiners.Add(new MinerList("xmrig.exe", "-o SERVER:PORT -u MONEROADDRESS -p x -k --safe", "-o SERVER:PORT -u MONEROADDRESS -p x -k --max-cpu-usage=30", true));
            gpuMiners.Add(new MinerList("miner.exe", "--server SERVER --port PORT --user EQUIHASHADDRESS --pass x --cuda_devices 0 --fee 0", "", false));
            ignoredFullscreenApps.Add("explorer");
            ignoredFullscreenApps.Add("LockApp");
            afterBurnerExePath = @"C:\Program Files (x86)\MSI Afterburner\MSIAfterburner.exe";
        }
    }
}