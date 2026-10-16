// Ignore Spelling: SkilledCarryWeight Jotunn

using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Extensions;
using Jotunn.Managers;
using Jotunn.Utils;
using Logging;
using System;
using System.Reflection;
using UnityEngine;


namespace SkilledCarryWeight {
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid, Jotunn.Main.Version)]
    [NetworkCompatibility(CompatibilityLevel.VersionCheckOnly, VersionStrictness.Patch)]
    [SynchronizationMode(AdminOnlyStrictness.IfOnServer)]
    internal sealed class QuickCart : BaseUnityPlugin {
        internal const string Author = "Searica";
        public const string PluginName = "QuickCart";
        public const string PluginGUID = $"{Author}.Valheim.{PluginName}";
        public const string PluginVersion = "1.0.1";


        internal static QuickCart Instance;

        internal static ConfigFileWatcher ConfigWatcher;
        internal static ConfigEntry<KeyCode> QuickCartKey;
        internal static ConfigEntry<float> AttachDistance;
        internal static ConfigEntry<bool> AttachOutOfPlace;
        internal static ConfigEntry<bool> AttachWhenCartFlipped;


        private const string MainSection = "Global";
        private const string CartSection = "Quick Cart";

        private static bool SettingsUpdated = false;

        public void Awake() {
            Instance = this;
            Log.Init(Logger);

            Config.SaveOnConfigSet = false;
            
            Log.Verbosity = Config.BindConfigInOrder(
                MainSection,
                "Verbosity",
                Log.InfoLevel.Low,
                "Low will log basic information about the mod. Medium will log information that " +
                "is useful for troubleshooting. High will log a lot of information, do not set " +
                "it to this without good reason as it will slow Down your game.",
                synced: false
            );

            Initialize();
            Config.Save();

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), harmonyInstanceId: PluginGUID);

            Game.isModded = true;

            ConfigWatcher = new ConfigFileWatcher(Config);

            // Update tools if config file reloaded
            ConfigWatcher.OnConfigFileReloaded += () =>
            {
                if (SettingsUpdated)
                {
                    Config.Save();
                    SettingsUpdated = false;
                }
            };

            // Update tools if in-game config manager window is closed
            SynchronizationManager.OnConfigurationWindowClosed += () =>
            {
                if (SettingsUpdated)
                {
                    Config.Save();
                    SettingsUpdated = false;
                }
            };

            // Update tools if in-game config manager window is closed
            SynchronizationManager.OnConfigurationSynchronized += (obj, args) =>
            {
                if (SettingsUpdated)
                {
                    Config.Save();
                    SettingsUpdated = false;
                }
            };
        }

        public void OnDestroy() {
            Config.Save();
        }

        private static void OnSettingChanged(object sender, EventArgs e) {
            if (!SettingsUpdated) { SettingsUpdated = true; }
        }

        /// <summary>
        ///     Set up configuration entries
        /// </summary>
        internal static void Initialize() {
            Log.Verbosity = Instance.Config.BindConfigInOrder(
                MainSection,
                "Verbosity",
                Log.InfoLevel.Low,
                "Low will log basic information about the mod. Medium will log information that " +
                "is useful for troubleshooting. High will log a lot of information, do not set " +
                "it to this without good reason as it will slow down your game.",
                synced: false
            );
            Log.Verbosity.SettingChanged += OnSettingChanged;

            QuickCartKey = Instance.Config.BindConfigInOrder(
                CartSection,
                "QuickCartKey",
                KeyCode.H,
                "The hotkey used to attach to or detach from a nearby cart.",
                synced: false
            );

            AttachDistance = Instance.Config.BindConfigInOrder(
                CartSection,
                "AttachDistance",
                5f,
                "Maximum distance to attach a cart from.",
                acceptableValues: new AcceptableValueRange<float>(2f, 8f),
                synced: true
            );

            AttachOutOfPlace = Instance.Config.BindConfigInOrder(
                CartSection,
                "AttachOutOfPlace",
                true,
                "Allow attaching to the cart even when you are not looking at the interaction hover text.",
                synced: true
            );


            AttachWhenCartFlipped = Instance.Config.BindConfigInOrder(
                CartSection,
                "AttachWhenCartFlipped",
                false,
                "Allow attaching to the cart even when it is flipped or otherwise out of position",
                synced: true
            );


        }

        /// <summary>
        ///     Check for quick cart attach/detatch
        /// </summary>
        private void Update() {
            if (Input.GetKeyDown(QuickCartKey.Value) && PlayerCanAttach() &&
                TryGetClosestVagon(out Vagon closestVagon) && closestVagon
            ) {
                closestVagon.Interact(Player.m_localPlayer, false, false);
            }
        }

        private static bool PlayerCanAttach() {
            return Player.m_localPlayer && !Player.m_localPlayer.IsDead() && !Player.m_localPlayer.InPlaceMode();
        }

        private static bool TryGetClosestVagon(out Vagon closestVagon) {
            closestVagon = null;
            float minDistance = float.PositiveInfinity;
            Vector3 position = Player.m_localPlayer.transform.position + Vector3.up;

            foreach (Collider collider in Physics.OverlapSphere(position, AttachDistance.Value)) {
                if (TryGetVagon(collider, out Vagon vagon) && collider.attachedRigidbody &&
                    (vagon.IsAttached(Player.m_localPlayer) || !vagon.InUse())
                ) {
                    float distance = Vector3.Distance(collider.ClosestPoint(position), position);
                    if (distance < minDistance) {
                        Log.LogInfo($"Found cart {distance} away", Log.InfoLevel.Medium);
                        minDistance = distance;
                        closestVagon = vagon;
                    }
                }
            }

            return minDistance < AttachDistance.Value;
        }

        private static bool TryGetVagon(Collider collider, out Vagon vagon) {
            if (!collider.TryGetComponent(out vagon) && collider.transform.parent) {
                vagon = collider.transform.parent.GetComponent<Vagon>();
            }
            return vagon;
        }

    }

    [HarmonyPatch]
    internal static class VagonPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Vagon), nameof(Vagon.CanAttach))]
        private static bool CanAttachPrefix(Vagon __instance, GameObject go, ref bool __result)
        {
            if (!QuickCart.AttachOutOfPlace.Value || (__instance.transform.up.y < 0.1f && !QuickCart.AttachWhenCartFlipped.Value) || go != Player.m_localPlayer.gameObject)
            {
                __instance.m_detachDistance = QuickCart.AttachDistance.Value;
                return true;
            }

            float distance = Vector3.Distance(
                go.transform.position + __instance.m_attachOffset,
                __instance.m_attachPoint.position
            );
            bool withinAttachDistance = distance < QuickCart.AttachDistance.Value;
            __result = !Player.m_localPlayer.IsTeleporting() && !Player.m_localPlayer.InDodge() && withinAttachDistance;
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Vagon), nameof(Vagon.GetHoverText))]
        private static void GetHoverTextPostfix(Vagon __instance, ref string __result)
        {
            __result = Localization.instance.Localize($"{__instance.m_name}\n[<color=yellow>{QuickCart.QuickCartKey.Value}</color>] $piece_use");
        }
    }
}

