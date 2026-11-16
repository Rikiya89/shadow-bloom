using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace ShadowBloom
{
    /// <summary>
    /// "Is an XR runtime driving this session?" for the Editor/desktop decisions made in Awake.
    /// XRSettings.isDeviceActive only turns true once the OpenXR session is running, which can take many seconds
    /// after the loader started (Meta XR Simulator: ~20 s). A loaded runtime keeps its display subsystem; a failed
    /// one (no headset, no simulator) is torn down by the loader, so the subsystem's existence is the earlier signal.
    /// </summary>
    public static class XRPresence
    {
        static readonly List<XRDisplaySubsystem> Displays = new List<XRDisplaySubsystem>();

        public static bool RuntimeLoaded
        {
            get
            {
                if (XRSettings.isDeviceActive) return true;
                SubsystemManager.GetSubsystems(Displays);
                return Displays.Count > 0;
            }
        }
    }
}
